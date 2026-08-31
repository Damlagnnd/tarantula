/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file    : main.c
  * @brief   : SLAVE Robot Firmware — Kusursuz Otonom (Ters Dönüş Fixli)
  ******************************************************************************
  */
/* USER CODE END Header */

#include "main.h"
#include "locomotion.h"
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <math.h>

#define BNO055_ADDR       (0x29 << 1)
#define BNO055_OPR_MODE   0x3D
#define BNO055_EULER_H_L  0x1A
#define BNO055_CALIB_STAT 0x35

#define ARRIVED_DIST_M  2.0
#define YAW_THRESHOLD   10.0
#define MIN_CAL_LEVEL   1

#define CMD_QUEUE_SIZE 16
static volatile uint8_t cmd_queue[CMD_QUEUE_SIZE];
static volatile uint8_t cmd_q_head = 0;
static volatile uint8_t cmd_q_tail = 0;

static void CmdQueue_Push(uint8_t cmd) {
    uint8_t next = (cmd_q_tail + 1) % CMD_QUEUE_SIZE;
    if (next == cmd_q_head) cmd_q_head = (cmd_q_head + 1) % CMD_QUEUE_SIZE;
    cmd_queue[cmd_q_tail] = cmd;
    cmd_q_tail = next;
}

static uint8_t CmdQueue_Pop(uint8_t *out) {
    if (cmd_q_head == cmd_q_tail) return 0;
    *out = cmd_queue[cmd_q_head];
    cmd_q_head = (cmd_q_head + 1) % CMD_QUEUE_SIZE;
    return 1;
}

I2C_HandleTypeDef hi2c1;
UART_HandleTypeDef huart2;
UART_HandleTypeDef huart6;

volatile uint8_t loco_busy = 0;

uint8_t addr_gs[] = {0x00, 0x01, 0x0C};

/* ── LoRa & GPS Değişkenleri ── */
uint8_t rx_lora, gps_rx;
char lora_pkt[96], lora_pkt_buf[96];
char gps_line[128], gps_buf[128];
uint8_t lora_pkt_idx = 0, lora_in_pkt = 0, gps_idx = 0;
volatile uint8_t lora_pkt_ready = 0, gps_ready = 0, gps_updated = 0;

double my_lat = 0.0, my_lon = 0.0, my_alt = 0.0, target_lat = 0.0, target_lon = 0.0;
uint8_t my_sats = 0, my_fix = 0, has_target = 0;
float bno_yaw = 0.0f, bno_pitch = 0.0f, bno_roll = 0.0f;
uint8_t bno_cal = 0, bno_sys_cal = 0, autonom_mode = 0;
char telem_buf[128];

/* Prototypes */
void SystemClock_Config(void);
void MX_GPIO_Init(void);
void MX_USART2_UART_Init(void);
void MX_I2C1_Init(void);
void MX_USART6_UART_Init(void);
void BNO055_Init(void);
void BNO055_Read(void);
void GPS_ParseNMEA(const char *line);
void ProcessLoraPacket(const char *pkt);
void SendTelemetry(void);
void RelayToGS(const char *msg, uint8_t len);
double Haversine(double la1, double lo1, double la2, double lo2);
double Bearing(double la1, double lo1, double la2, double lo2);

int main(void)
{
    HAL_Init();
    SystemClock_Config();
    MX_GPIO_Init();
    MX_USART2_UART_Init();
    MX_I2C1_Init();
    MX_USART6_UART_Init();

    BNO055_Init();
    PCA9685_Init();
    HAL_Delay(200);
    Hexapod_StandUp();

    HAL_UART_Receive_IT(&huart6, &rx_lora, 1);
    HAL_UART_Receive_IT(&huart2, &gps_rx,  1);

    HAL_Delay(200);
    RelayToGS("$S:LOG,SLAVE READY\r\n", 19);

    while (1)
    {
        if (lora_pkt_ready) { lora_pkt_ready = 0; ProcessLoraPacket(lora_pkt_buf); }
        if (gps_ready) { gps_ready = 0; GPS_ParseNMEA(gps_line); gps_updated = 1; }

        if (!loco_busy)
        {
            uint8_t cmd = 0;
            if (CmdQueue_Pop(&cmd))
            {
                switch (cmd)
                {
                    case '?': BNO055_Read(); SendTelemetry(); break;
                    case 'F': Hexapod_StepForward();  break;
                    case 'B': Hexapod_StepBackward(); break;
                    case 'L': Hexapod_TurnLeft();     break;
                    case 'R': Hexapod_TurnRight();    break;
                    case 'S': Hexapod_Stop();         break;
                    case 'A': autonom_mode = 0; Hexapod_Stop(); break;
                    case 'G':
                        if (has_target && my_fix && bno_sys_cal >= MIN_CAL_LEVEL) {
                            autonom_mode = 1; RelayToGS("$S:NAV\n", 7);
                        }
                        break;
                }
            }
        }

        /* ── Otonom navigasyon (Körleme ve Ters Dönüş Fixli) ── */
        if (autonom_mode && has_target && my_fix && !loco_busy)
        {
            // Otonom karardan hemen önce yönü tazele
            BNO055_Read();

            double dist = Haversine(my_lat, my_lon, target_lat, target_lon);
            double bearing = Bearing(my_lat, my_lon, target_lat, target_lon);
            double yaw_err = bearing - (double)bno_yaw;
            while (yaw_err >  180.0) yaw_err -= 360.0;
            while (yaw_err < -180.0) yaw_err += 360.0;

            if (dist < ARRIVED_DIST_M) { autonom_mode = 0; Hexapod_Stop(); RelayToGS("$S:ARR\n", 7); }
            else if (yaw_err >  YAW_THRESHOLD) Hexapod_TurnRight();
            else if (yaw_err < -YAW_THRESHOLD) Hexapod_TurnLeft();
            else Hexapod_StepForward();
        }
    }
}

void HAL_UART_RxCpltCallback(UART_HandleTypeDef *huart) {
    if (huart->Instance == USART6) {
        char c = (char)rx_lora;
        if (c == '$') { lora_pkt_idx = 0; lora_in_pkt = 1; lora_pkt[lora_pkt_idx++] = c; }
        else if (lora_in_pkt) {
            if (lora_pkt_idx < 94) lora_pkt[lora_pkt_idx++] = c;
            if (c == '\n') {
                lora_pkt[lora_pkt_idx] = '\0'; memcpy(lora_pkt_buf, lora_pkt, lora_pkt_idx + 1);
                lora_pkt_ready = 1; lora_in_pkt = 0;
            }
        }
        else if ((c >= 'A' && c <= 'Z') || c == '?') {
            CmdQueue_Push((uint8_t)c);
        }
        HAL_UART_Receive_IT(&huart6, &rx_lora, 1);
    }
    if (huart->Instance == USART2) {
        char c = (char)gps_rx;
        if (c == '\n') { gps_buf[gps_idx] = '\0'; memcpy(gps_line, gps_buf, gps_idx + 1); gps_ready = 1; gps_idx = 0; }
        else if (c != '\r' && gps_idx < 126) gps_buf[gps_idx++] = c;
        HAL_UART_Receive_IT(&huart2, &gps_rx, 1);
    }
}

void HAL_UART_ErrorCallback(UART_HandleTypeDef *huart) {
    if (huart->ErrorCode & HAL_UART_ERROR_ORE) {
        __HAL_UART_CLEAR_OREFLAG(huart);
        if (huart->Instance == USART6) HAL_UART_Receive_IT(&huart6, &rx_lora, 1);
        if (huart->Instance == USART2) HAL_UART_Receive_IT(&huart2, &gps_rx, 1);
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
    HAL_Delay(25);
}

void SendTelemetry(void) {
    if (loco_busy) return;
    int len = snprintf(telem_buf, sizeof(telem_buf), "$S:GPS,%.6f,%.6f,%.1f,%d,%d\n", my_lat, my_lon, my_alt, my_sats, my_fix);
    RelayToGS(telem_buf, (uint8_t)len);
    len = snprintf(telem_buf, sizeof(telem_buf), "$S:BNO,%.1f,%.1f,%.1f,%d,%d,%d,%d\n", bno_yaw, bno_pitch, bno_roll, (bno_cal>>6)&3, (bno_cal>>2)&3, (bno_cal>>4)&3, bno_cal&3);
    RelayToGS(telem_buf, (uint8_t)len);
}

void GPS_ParseNMEA(const char *line) {
    if (strncmp(line, "$GPGGA", 6) != 0 && strncmp(line, "$GNGGA", 6) != 0) return;
    char buf[128]; strncpy(buf, line, 127);
    char *f[15]; int fi = 0; char *tok = strtok(buf, ",");
    while (tok && fi < 15) { f[fi++] = tok; tok = strtok(NULL, ","); }
    if (fi < 10) return;
    my_fix = atoi(f[6]) > 0 ? 1 : 0; my_sats = (uint8_t)atoi(f[7]); my_alt = atof(f[9]);
    if (strlen(f[1]) > 4) { double r = atof(f[1]); int d = (int)(r / 100); my_lat = d + (r - d * 100) / 60.0; if (f[2][0] == 'S') my_lat = -my_lat; }
    if (strlen(f[3]) > 4) { double r = atof(f[3]); int d = (int)(r / 100); my_lon = d + (r - d * 100) / 60.0; if (f[4][0] == 'W') my_lon = -my_lon; }
}

void ProcessLoraPacket(const char *pkt) {
    if (strncmp(pkt, "$T:", 3) == 0) {
        char buf[64]; strncpy(buf, pkt + 3, 63);
        char *comma = strchr(buf, ',');
        if (comma) { *comma = '\0'; target_lat = atof(buf); target_lon = atof(comma + 1); has_target = 1; }
    }
}

double Haversine(double la1, double lo1, double la2, double lo2) {
    double dLa = (la2-la1)*M_PI/180.0, dLo = (lo2-lo1)*M_PI/180.0;
    double a = sin(dLa/2)*sin(dLa/2) + cos(la1*M_PI/180.0)*cos(la2*M_PI/180.0)*sin(dLo/2)*sin(dLo/2);
    return 6371000.0 * 2.0 * atan2(sqrt(a), sqrt(1.0-a));
}

double Bearing(double la1, double lo1, double la2, double lo2) {
    double dLo = (lo2-lo1)*M_PI/180.0;
    double y = sin(dLo)*cos(la2*M_PI/180.0);
    double x = cos(la1*M_PI/180.0)*sin(la2*M_PI/180.0) - sin(la1*M_PI/180.0)*cos(la2*M_PI/180.0)*cos(dLo);
    return fmod(atan2(y,x)*180.0/M_PI + 360.0, 360.0);
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
void MX_USART2_UART_Init(void) { huart2.Instance = USART2; huart2.Init.BaudRate = 115200; huart2.Init.WordLength = UART_WORDLENGTH_8B; huart2.Init.StopBits = UART_STOPBITS_1; huart2.Init.Parity = UART_PARITY_NONE; huart2.Init.Mode = UART_MODE_TX_RX; huart2.Init.HwFlowCtl = UART_HWCONTROL_NONE; huart2.Init.OverSampling = UART_OVERSAMPLING_16; HAL_UART_Init(&huart2); }
void MX_USART6_UART_Init(void) { huart6.Instance = USART6; huart6.Init.BaudRate = 9600; huart6.Init.WordLength = UART_WORDLENGTH_8B; huart6.Init.StopBits = UART_STOPBITS_1; huart6.Init.Parity = UART_PARITY_NONE; huart6.Init.Mode = UART_MODE_TX_RX; huart6.Init.HwFlowCtl = UART_HWCONTROL_NONE; huart6.Init.OverSampling = UART_OVERSAMPLING_16; HAL_UART_Init(&huart6); }
void MX_GPIO_Init(void) { GPIO_InitTypeDef GPIO_InitStruct = {0}; __HAL_RCC_GPIOC_CLK_ENABLE(); __HAL_RCC_GPIOA_CLK_ENABLE(); __HAL_RCC_GPIOB_CLK_ENABLE(); HAL_GPIO_WritePin(GPIOC, GPIO_PIN_13, GPIO_PIN_RESET); GPIO_InitStruct.Pin = GPIO_PIN_13; GPIO_InitStruct.Mode = GPIO_MODE_OUTPUT_PP; GPIO_InitStruct.Pull = GPIO_NOPULL; GPIO_InitStruct.Speed = GPIO_SPEED_FREQ_LOW; HAL_GPIO_Init(GPIOC, &GPIO_InitStruct); }
void Error_Handler(void) { __disable_irq(); while (1) {} }
