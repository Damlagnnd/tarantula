/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file    : main.c
  * @brief   : Master Robot Firmware — GPS'siz İşaretleyici (Designator) Modu
  ******************************************************************************
  */
/* USER CODE END Header */

#include "main.h"
#include "locomotion.h"
#include <string.h>
#include <stdio.h>
#include <stdlib.h>

/* ── Matematik kütüphanesine (math.h) ve GPS hesaplamalarına artık gerek yok! ── */

#define BNO055_ADDR       (0x29 << 1)
#define BNO055_OPR_MODE   0x3D
#define BNO055_EULER_H_L  0x1A
#define BNO055_CALIB_STAT 0x35

#define MIN_CAL_LEVEL   1
#define TELEM_INTERVAL  1200

/* ── Spam Filtresi ── */
volatile uint8_t active_cmd = 0;

I2C_HandleTypeDef hi2c1;
UART_HandleTypeDef huart1;
UART_HandleTypeDef huart2; // GPS söküldü ama kod hata vermesin diye tanımı duruyor
UART_HandleTypeDef huart6;

volatile uint8_t loco_busy = 0;

uint8_t addr_gs[]    = {0x00, 0x01, 0x0C};
uint8_t addr_slave[] = {0x00, 0x03, 0x0C};

/* ── LoRa & RPi Değişkenleri (GPS Silindi) ── */
uint8_t rx_lora, rx_rpi;
char lora_pkt[96], lora_pkt_buf[96];
char rpi_line[128], rpi_buf[128];
uint8_t lora_pkt_idx = 0, lora_in_pkt = 0, rpi_idx = 0;
volatile uint8_t lora_pkt_ready = 0, rpi_ready = 0;

double target_lat = 0.0, target_lon = 0.0;
uint8_t has_target = 0;
float bno_yaw = 0.0f, bno_pitch = 0.0f, bno_roll = 0.0f;
uint8_t bno_cal = 0, bno_sys_cal = 0, autonom_mode = 0;
uint32_t last_telem = 0;
char telem_buf[128];

/* Prototypes */
void SystemClock_Config(void);
void MX_GPIO_Init(void);
void MX_USART1_UART_Init(void);
void MX_USART2_UART_Init(void);
void MX_I2C1_Init(void);
void MX_USART6_UART_Init(void);
void BNO055_Init(void);
void BNO055_Read(void);
void SendTelemetry(void);
void RelayToGS(const char *msg, uint8_t len);
void SendToSlave(const char *msg, uint8_t len);

int main(void)
{
    HAL_Init();
    SystemClock_Config();
    MX_GPIO_Init();
    MX_USART1_UART_Init();
    MX_USART2_UART_Init();
    MX_I2C1_Init();
    MX_USART6_UART_Init();

    BNO055_Init();
    PCA9685_Init();
    HAL_Delay(200);
    Hexapod_StandUp();

    HAL_UART_Receive_IT(&huart6, &rx_lora, 1);
    // GPS UART dinlemesi iptal edildi!
    HAL_UART_Receive_IT(&huart1, &rx_rpi,  1);

    RelayToGS("$M:LOG,MASTER READY (NO GPS)\r\n", 28);

    while (1)
    {
        uint32_t now = HAL_GetTick();

        /* ── RPi (Kamera/QR) İşleme ── */
        if (rpi_ready) {
            rpi_ready = 0;
            if (strncmp(rpi_line, "$QR:", 4) == 0) {
                char buf[64]; strncpy(buf, rpi_line + 4, 63);
                char *comma = strchr(buf, ',');
                if (comma) {
                    *comma = '\0';
                    target_lat = atof(buf);
                    target_lon = atof(comma + 1);
                    has_target = 1;

                    // Arayüze QR'ın okunduğunu bildir
                    RelayToGS(rpi_line, strlen(rpi_line));

                    // Hedefi Slave'e fırlat
                    char slave_msg[64];
                    int len = snprintf(slave_msg, sizeof(slave_msg), "$T:%.6f,%.6f\n", target_lat, target_lon);
                    SendToSlave(slave_msg, len);
                }
            }
        }

        if (lora_pkt_ready) { lora_pkt_ready = 0; } // Master'ın artık gelen hedefleri işlemesine gerek yok

        /* ── Spam Filtreli Komut İşleme ── */
        if (!loco_busy && active_cmd != 0)
        {
            uint8_t cmd = active_cmd;
            active_cmd = 0;

            switch (cmd)
            {
                case 'F': SendToSlave("F", 1); Hexapod_StepForward();  break;
                case 'B': SendToSlave("B", 1); Hexapod_StepBackward(); break;
                case 'L': SendToSlave("L", 1); Hexapod_TurnLeft();     break;
                case 'R': SendToSlave("R", 1); Hexapod_TurnRight();    break;
                case 'S': SendToSlave("S", 1); Hexapod_Stop();         break;
                case 'A': SendToSlave("A", 1); autonom_mode = 0; Hexapod_Stop(); break;
                case 'G':
                    if (has_target && bno_sys_cal >= MIN_CAL_LEVEL) { // GPS (my_fix) şartı kaldırıldı
                        autonom_mode = 1;
                        SendToSlave("G", 1); // Slave'e "Hadi aslanım göreve" der
                        Hexapod_Stop();      // YENİ: Master olduğu yere çakılır, hareket etmez!
                        RelayToGS("$M:ARR\n", 7); // Arayüze "Ben hedeftemişim gibi göster" der
                    } else {
                        RelayToGS("$M:WARN,NOT READY\r\n", 19);
                    }
                    break;
            }
            last_telem = HAL_GetTick();
        }

        /* ── Otonom navigasyon (SADECE BEKLEME MODU) ── */
        if (autonom_mode)
        {
            // Master otonom moddayken HİÇBİR ŞEY YAPMAZ.
            // Sadece A (Abort) komutunu bekler.
        }

        /* ── Telemetri ── */
        if (loco_busy) { last_telem = now; }
        else if ((now - last_telem) >= TELEM_INTERVAL) {
            last_telem = now;
            BNO055_Read();
            SendTelemetry();
        }
    }
}

/* ════════════════════════════════════════════════
   UART CALLBACK (GPS SİLİNDİ)
   ════════════════════════════════════════════════ */
void HAL_UART_RxCpltCallback(UART_HandleTypeDef *huart) {
    if (huart->Instance == USART6) {
        char c = (char)rx_lora;
        if (c == '$') { lora_pkt_idx = 0; lora_in_pkt = 1; lora_pkt[lora_pkt_idx++] = c; }
        else if (lora_in_pkt) {
            if (lora_pkt_idx < 94) lora_pkt[lora_pkt_idx++] = c;
            if (c == '\n') { lora_pkt[lora_pkt_idx] = '\0'; memcpy(lora_pkt_buf, lora_pkt, lora_pkt_idx + 1); lora_pkt_ready = 1; lora_in_pkt = 0; }
        }
        else if (c == 'F' || c == 'B' || c == 'L' || c == 'R' || c == 'S' || c == 'A' || c == 'G') {
            active_cmd = (uint8_t)c;
        }
        HAL_UART_Receive_IT(&huart6, &rx_lora, 1);
    }
    if (huart->Instance == USART1) {
        char c = (char)rx_rpi;
        if (c == '\n') { rpi_buf[rpi_idx] = '\n'; rpi_buf[rpi_idx + 1] = '\0'; memcpy(rpi_line, rpi_buf, rpi_idx + 2); rpi_ready = 1; rpi_idx = 0; }
        else if (c != '\r' && rpi_idx < 126) rpi_buf[rpi_idx++] = c;
        HAL_UART_Receive_IT(&huart1, &rx_rpi, 1);
    }
}

void HAL_UART_ErrorCallback(UART_HandleTypeDef *huart) {
    if (huart->ErrorCode & HAL_UART_ERROR_ORE) {
        __HAL_UART_CLEAR_OREFLAG(huart);
        if (huart->Instance == USART6) HAL_UART_Receive_IT(&huart6, &rx_lora, 1);
        if (huart->Instance == USART1) HAL_UART_Receive_IT(&huart1, &rx_rpi, 1);
    }
}

void BNO055_Init(void) {
    uint8_t mode = 0x0C; HAL_I2C_Mem_Write(&hi2c1, BNO055_ADDR, BNO055_OPR_MODE, 1, &mode, 1, 30); HAL_Delay(700);
}

void BNO055_Read(void) {
    static uint8_t fail_count = 0; uint8_t current_mode = 0;
    if (HAL_I2C_Mem_Read(&hi2c1, BNO055_ADDR, BNO055_OPR_MODE, 1, &current_mode, 1, 20) == HAL_OK) {
        if (current_mode != 0x0C) { BNO055_Init(); return; }
    }
    uint8_t raw[6];
    if (HAL_I2C_Mem_Read(&hi2c1, BNO055_ADDR, BNO055_EULER_H_L, 1, raw, 6, 50) == HAL_OK) {
        int16_t raw_yaw = (int16_t)((raw[1] << 8) | raw[0]); int16_t raw_roll = (int16_t)((raw[3] << 8) | raw[2]); int16_t raw_pitch = (int16_t)((raw[5] << 8) | raw[4]);
        if (raw_yaw != 0 || raw_roll != 0 || raw_pitch != 0) { bno_yaw = raw_yaw / 16.0f; bno_roll = raw_roll / 16.0f; bno_pitch = raw_pitch / 16.0f; }
        HAL_I2C_Mem_Read(&hi2c1, BNO055_ADDR, BNO055_CALIB_STAT, 1, &bno_cal, 1, 20);
        bno_sys_cal = (bno_cal >> 6) & 0x03; fail_count = 0; HAL_GPIO_TogglePin(GPIOC, GPIO_PIN_13);
    } else {
        fail_count++; HAL_GPIO_WritePin(GPIOC, GPIO_PIN_13, GPIO_PIN_SET);
        if (fail_count >= 3) { fail_count = 0; HAL_I2C_DeInit(&hi2c1); HAL_Delay(10); MX_I2C1_Init(); HAL_Delay(10); BNO055_Init(); }
    }
}

void RelayToGS(const char *msg, uint8_t len) {
    HAL_UART_Transmit(&huart6, addr_gs, 3, 1000);
    HAL_UART_Transmit(&huart6, (uint8_t*)msg, len, 1000);
    HAL_Delay(20);
}

void SendToSlave(const char *msg, uint8_t len) {
    HAL_UART_Transmit(&huart6, addr_slave, 3, 1000);
    HAL_UART_Transmit(&huart6, (uint8_t*)msg, len, 1000);
    HAL_Delay(80);
}

void SendTelemetry(void) {
    if (loco_busy) return;

    // C# arayüzü çökmesin diye sahte (0.0) GPS verisi gönderiliyor
    int len = snprintf(telem_buf, sizeof(telem_buf), "$M:GPS,0.000000,0.000000,0.0,0,0\n");
    RelayToGS(telem_buf, (uint8_t)len);

    len = snprintf(telem_buf, sizeof(telem_buf), "$M:BNO,%.1f,%.1f,%.1f,%d,%d,%d,%d\n", bno_yaw, bno_pitch, bno_roll, (bno_cal>>6)&3, (bno_cal>>2)&3, (bno_cal>>4)&3, bno_cal&3);
    RelayToGS(telem_buf, (uint8_t)len);

    SendToSlave("?", 1);
}

void SystemClock_Config(void) {
    RCC_OscInitTypeDef RCC_OscInitStruct = {0}; RCC_ClkInitTypeDef RCC_ClkInitStruct = {0};
    __HAL_RCC_PWR_CLK_ENABLE(); __HAL_PWR_VOLTAGESCALING_CONFIG(PWR_REGULATOR_VOLTAGE_SCALE2);
    RCC_OscInitStruct.OscillatorType = RCC_OSCILLATORTYPE_HSI; RCC_OscInitStruct.HSIState = RCC_HSI_ON; RCC_OscInitStruct.HSICalibrationValue = RCC_HSICALIBRATION_DEFAULT; RCC_OscInitStruct.PLL.PLLState = RCC_PLL_NONE;
    HAL_RCC_OscConfig(&RCC_OscInitStruct);
    RCC_ClkInitStruct.ClockType = RCC_CLOCKTYPE_HCLK | RCC_CLOCKTYPE_SYSCLK | RCC_CLOCKTYPE_PCLK1 | RCC_CLOCKTYPE_PCLK2;
    RCC_ClkInitStruct.SYSCLKSource = RCC_SYSCLKSOURCE_HSI; RCC_ClkInitStruct.AHBCLKDivider = RCC_SYSCLK_DIV1; RCC_ClkInitStruct.APB1CLKDivider = RCC_HCLK_DIV1; RCC_ClkInitStruct.APB2CLKDivider = RCC_HCLK_DIV1;
    HAL_RCC_ClockConfig(&RCC_ClkInitStruct, FLASH_LATENCY_0);
}
void MX_I2C1_Init(void) { hi2c1.Instance = I2C1; hi2c1.Init.ClockSpeed = 100000; hi2c1.Init.DutyCycle = I2C_DUTYCYCLE_2; hi2c1.Init.OwnAddress1 = 0; hi2c1.Init.AddressingMode = I2C_ADDRESSINGMODE_7BIT; hi2c1.Init.DualAddressMode = I2C_DUALADDRESS_DISABLE; hi2c1.Init.OwnAddress2 = 0; hi2c1.Init.GeneralCallMode = I2C_GENERALCALL_DISABLE; hi2c1.Init.NoStretchMode = I2C_NOSTRETCH_DISABLE; HAL_I2C_Init(&hi2c1); }
void MX_USART1_UART_Init(void) { huart1.Instance = USART1; huart1.Init.BaudRate = 9600; huart1.Init.WordLength = UART_WORDLENGTH_8B; huart1.Init.StopBits = UART_STOPBITS_1; huart1.Init.Parity = UART_PARITY_NONE; huart1.Init.Mode = UART_MODE_TX_RX; huart1.Init.HwFlowCtl = UART_HWCONTROL_NONE; huart1.Init.OverSampling = UART_OVERSAMPLING_16; HAL_UART_Init(&huart1); }
void MX_USART2_UART_Init(void) { huart2.Instance = USART2; huart2.Init.BaudRate = 115200; huart2.Init.WordLength = UART_WORDLENGTH_8B; huart2.Init.StopBits = UART_STOPBITS_1; huart2.Init.Parity = UART_PARITY_NONE; huart2.Init.Mode = UART_MODE_TX_RX; huart2.Init.HwFlowCtl = UART_HWCONTROL_NONE; huart2.Init.OverSampling = UART_OVERSAMPLING_16; HAL_UART_Init(&huart2); }
void MX_USART6_UART_Init(void) { huart6.Instance = USART6; huart6.Init.BaudRate = 9600; huart6.Init.WordLength = UART_WORDLENGTH_8B; huart6.Init.StopBits = UART_STOPBITS_1; huart6.Init.Parity = UART_PARITY_NONE; huart6.Init.Mode = UART_MODE_TX_RX; huart6.Init.HwFlowCtl = UART_HWCONTROL_NONE; huart6.Init.OverSampling = UART_OVERSAMPLING_16; HAL_UART_Init(&huart6); }
void MX_GPIO_Init(void) { GPIO_InitTypeDef GPIO_InitStruct = {0}; __HAL_RCC_GPIOC_CLK_ENABLE(); __HAL_RCC_GPIOA_CLK_ENABLE(); __HAL_RCC_GPIOB_CLK_ENABLE(); HAL_GPIO_WritePin(GPIOC, GPIO_PIN_13, GPIO_PIN_RESET); GPIO_InitStruct.Pin = GPIO_PIN_13; GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP; GPIO_InitStruct.Pull = GPIO_NOPULL; GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW; HAL_GPIO_Init(GPIOC, &GPIO_InitStruct); }
void Error_Handler(void) { __disable_irq(); while (1) {} }
