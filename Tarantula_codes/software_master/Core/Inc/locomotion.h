#ifndef LOCOMOTION_H
#define LOCOMOTION_H

#include "stm32f4xx_hal.h"
#include <stdint.h>

/* ── PCA9685 ── */
#define PCA9685_ADDR      (0x40 << 1)
#define PCA9685_MODE1     0x00
#define PCA9685_PRESCALE  0xFE
#define PCA9685_LED0_ON_L 0x06

/* ── PWM tick değerleri (MG90S @ 50Hz) ── */
#define COXA_NEUTRAL   307
#define COXA_FORWARD   370
#define COXA_BACKWARD  244
#define FEMUR_STAND    250
#define FEMUR_LIFT     370
#define PHASE_MS       120
#define SETTLE_MS       60

/* ── Bacak grupları (tripod gait) ── */
static const uint8_t GROUP_A[3] = {0, 2, 4};
static const uint8_t GROUP_B[3] = {1, 3, 5};

extern I2C_HandleTypeDef hi2c1;
extern volatile uint8_t loco_busy;

static void I2C_Recover(void) {
    __HAL_RCC_I2C1_FORCE_RESET();
    HAL_Delay(10);
    __HAL_RCC_I2C1_RELEASE_RESET();
    HAL_Delay(5);

    hi2c1.Instance             = I2C1;
    hi2c1.Init.ClockSpeed      = 100000;
    hi2c1.Init.DutyCycle       = I2C_DUTYCYCLE_2;
    hi2c1.Init.OwnAddress1     = 0;
    hi2c1.Init.AddressingMode  = I2C_ADDRESSINGMODE_7BIT;
    hi2c1.Init.DualAddressMode = I2C_DUALADDRESS_DISABLE;
    hi2c1.Init.OwnAddress2     = 0;
    hi2c1.Init.GeneralCallMode = I2C_GENERALCALL_DISABLE;
    hi2c1.Init.NoStretchMode   = I2C_NOSTRETCH_DISABLE;
    HAL_I2C_Init(&hi2c1);
    HAL_Delay(5);

    uint8_t b[2] = {PCA9685_MODE1, 0xA0};
    HAL_I2C_Master_Transmit(&hi2c1, PCA9685_ADDR, b, 2, 30);
}

static void PCA9685_Write(uint8_t reg, uint8_t val) {
    uint8_t b[2] = {reg, val};
    if (HAL_I2C_Master_Transmit(&hi2c1, PCA9685_ADDR, b, 2, 30) != HAL_OK)
        I2C_Recover();
}

static void PCA9685_SetCh(uint8_t ch, uint16_t on, uint16_t off) {
    uint8_t reg = PCA9685_LED0_ON_L + ch * 4;
    uint8_t b[5] = {reg, on & 0xFF, on >> 8, off & 0xFF, off >> 8};
    if (HAL_I2C_Master_Transmit(&hi2c1, PCA9685_ADDR, b, 5, 30) != HAL_OK)
        I2C_Recover();
}

static void Servo(uint8_t ch, uint16_t t)   { PCA9685_SetCh(ch, 0, t); }

/* ── GÜNCEL SIRA: Çift Pinler DİZ (Femur), Tek Pinler GÖVDE (Coxa) ── */
static void LegLift(uint8_t l)              { Servo(l*2, FEMUR_LIFT);  } // Önce Diz
static void LegDown(uint8_t l)              { Servo(l*2, FEMUR_STAND); } // Önce Diz

static void LegFwd(uint8_t l) {
    if (l < 3) Servo(l*2+1, COXA_FORWARD);  // Sonra Gövde
    else       Servo(l*2+1, COXA_BACKWARD); // Sol bacaklar
}

static void LegBack(uint8_t l) {
    if (l < 3) Servo(l*2+1, COXA_BACKWARD); // Sonra Gövde
    else       Servo(l*2+1, COXA_FORWARD);  // Sol bacaklar
}

static void LegNeutral(uint8_t l)           { Servo(l*2+1, COXA_NEUTRAL); Servo(l*2, FEMUR_STAND); }

void PCA9685_Init(void) {
    PCA9685_Write(PCA9685_MODE1, 0x00); HAL_Delay(10);
    uint8_t allOff[5] = {0xFA, 0x00, 0x00, 0x00, 0x10};
    HAL_I2C_Master_Transmit(&hi2c1, PCA9685_ADDR, allOff, 5, 30);
    HAL_Delay(5);
    PCA9685_Write(PCA9685_MODE1, 0x10);
    PCA9685_Write(PCA9685_PRESCALE, 0x79);
    PCA9685_Write(PCA9685_MODE1, 0x00); HAL_Delay(5);
    PCA9685_Write(PCA9685_MODE1, 0xA0);
    HAL_Delay(5);
}

void Hexapod_StandUp(void) {
    loco_busy = 1;
    for (uint8_t i = 0; i < 6; i++) LegNeutral(i);
    HAL_Delay(500);
    loco_busy = 0;
}

void Hexapod_StepForward(void) {
    loco_busy = 1;

    for (int i = 0; i < 3; i++) LegLift(GROUP_A[i]);
    HAL_Delay(SETTLE_MS);
    for (int i = 0; i < 3; i++) LegFwd(GROUP_A[i]);
    for (int i = 0; i < 3; i++) LegBack(GROUP_B[i]);
    HAL_Delay(PHASE_MS);
    for (int i = 0; i < 3; i++) LegDown(GROUP_A[i]);
    HAL_Delay(SETTLE_MS);

    for (int i = 0; i < 3; i++) LegLift(GROUP_B[i]);
    HAL_Delay(SETTLE_MS);
    for (int i = 0; i < 3; i++) LegFwd(GROUP_B[i]);
    for (int i = 0; i < 3; i++) LegBack(GROUP_A[i]);
    HAL_Delay(PHASE_MS);
    for (int i = 0; i < 3; i++) LegDown(GROUP_B[i]);
    HAL_Delay(SETTLE_MS);

    // Gövde (Coxa) motorları artık tek sayılarda olduğu için i*2+1 yapıldı
    for (uint8_t i = 0; i < 6; i++) Servo(i*2+1, COXA_NEUTRAL);
    HAL_Delay(SETTLE_MS);

    loco_busy = 0;
}

void Hexapod_StepBackward(void) {
    loco_busy = 1;

    for (int i = 0; i < 3; i++) LegLift(GROUP_A[i]);
    HAL_Delay(SETTLE_MS);
    for (int i = 0; i < 3; i++) LegBack(GROUP_A[i]);
    for (int i = 0; i < 3; i++) LegFwd(GROUP_B[i]);
    HAL_Delay(PHASE_MS);
    for (int i = 0; i < 3; i++) LegDown(GROUP_A[i]);
    HAL_Delay(SETTLE_MS);

    for (int i = 0; i < 3; i++) LegLift(GROUP_B[i]);
    HAL_Delay(SETTLE_MS);
    for (int i = 0; i < 3; i++) LegBack(GROUP_B[i]);
    for (int i = 0; i < 3; i++) LegFwd(GROUP_A[i]);
    HAL_Delay(PHASE_MS);
    for (int i = 0; i < 3; i++) LegDown(GROUP_B[i]);
    HAL_Delay(SETTLE_MS);

    for (uint8_t i = 0; i < 6; i++) Servo(i*2+1, COXA_NEUTRAL);
    HAL_Delay(SETTLE_MS);

    loco_busy = 0;
}

void Hexapod_TurnLeft(void) {
    loco_busy = 1;

    for (int i = 0; i < 3; i++) LegLift(GROUP_A[i]);
    HAL_Delay(SETTLE_MS);
    for (int i = 0; i < 3; i++) {
        if (GROUP_A[i] < 3) LegFwd(GROUP_A[i]);
        else                LegBack(GROUP_A[i]);
    }
    for (int i = 0; i < 3; i++) {
        if (GROUP_B[i] < 3) LegBack(GROUP_B[i]);
        else                LegFwd(GROUP_B[i]);
    }
    HAL_Delay(PHASE_MS);
    for (int i = 0; i < 3; i++) LegDown(GROUP_A[i]);
    HAL_Delay(SETTLE_MS);

    for (int i = 0; i < 3; i++) LegLift(GROUP_B[i]);
    HAL_Delay(SETTLE_MS);
    for (int i = 0; i < 3; i++) {
        if (GROUP_B[i] < 3) LegFwd(GROUP_B[i]);
        else                LegBack(GROUP_B[i]);
    }
    for (int i = 0; i < 3; i++) {
        if (GROUP_A[i] < 3) LegBack(GROUP_A[i]);
        else                LegFwd(GROUP_A[i]);
    }
    HAL_Delay(PHASE_MS);
    for (int i = 0; i < 3; i++) LegDown(GROUP_B[i]);
    HAL_Delay(SETTLE_MS);

    for (uint8_t i = 0; i < 6; i++) Servo(i*2+1, COXA_NEUTRAL);
    HAL_Delay(SETTLE_MS);

    loco_busy = 0;
}

void Hexapod_TurnRight(void) {
    loco_busy = 1;

    for (int i = 0; i < 3; i++) LegLift(GROUP_A[i]);
    HAL_Delay(SETTLE_MS);
    for (int i = 0; i < 3; i++) {
        if (GROUP_A[i] < 3) LegBack(GROUP_A[i]);
        else                LegFwd(GROUP_A[i]);
    }
    for (int i = 0; i < 3; i++) {
        if (GROUP_B[i] < 3) LegFwd(GROUP_B[i]);
        else                LegBack(GROUP_B[i]);
    }
    HAL_Delay(PHASE_MS);
    for (int i = 0; i < 3; i++) LegDown(GROUP_A[i]);
    HAL_Delay(SETTLE_MS);

    for (int i = 0; i < 3; i++) LegLift(GROUP_B[i]);
    HAL_Delay(SETTLE_MS);
    for (int i = 0; i < 3; i++) {
        if (GROUP_B[i] < 3) LegBack(GROUP_B[i]);
        else                LegFwd(GROUP_B[i]);
    }
    for (int i = 0; i < 3; i++) {
        if (GROUP_A[i] < 3) LegFwd(GROUP_A[i]);
        else                LegBack(GROUP_A[i]);
    }
    HAL_Delay(PHASE_MS);
    for (int i = 0; i < 3; i++) LegDown(GROUP_B[i]);
    HAL_Delay(SETTLE_MS);

    for (uint8_t i = 0; i < 6; i++) Servo(i*2+1, COXA_NEUTRAL);
    HAL_Delay(SETTLE_MS);

    loco_busy = 0;
}

void Hexapod_Stop(void)            { Hexapod_StandUp(); }
void Hexapod_WalkNSteps(uint8_t n) { for (uint8_t i = 0; i < n; i++) Hexapod_StepForward(); }

#endif /* LOCOMOTION_H */
