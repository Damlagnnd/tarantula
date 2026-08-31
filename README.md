# Tarantula

Tarantula, STM32 tabanlı altı bacaklı (hexapod) bir robotun otonom hareketi, uzaktan haberleşmesi, QR kod algılaması ve Unity tabanlı dijital ikizi için kaynak kodları içeren bir proje arşividir.

## Kapsam

- **Robot firmware'i:** GPS hedefi ve BNO055 yönelim verisini kullanarak otonom yön bulma; PCA9685 üzerinden 12 servo kontrolü.
- **Yer istasyonu:** PC ile robot tarafındaki LoRa bağlantısı arasında UART köprüsü.
- **Raspberry Pi uygulaması:** Kamera görüntüsündeki QR kodları algılar, UART ile STM32'ye iletir ve Flask üzerinden canlı görüntü sağlar.
- **Unity simülasyonu:** Robot hareketi, servo açıları ve kampüs ortamı için dijital ikiz bileşenleri.

## Dizin yapısı

```text
.
├── Tarantula_IEEE.pdf                 # IEEE biçimindeki proje raporu
├── Tarantula_SRS.pdf                  # Yazılım gereksinimleri dokümanı
├── Demo/                              # Demo içerikleri
└── Tarantula_codes/
    ├── SoftwareProject/               # STM32 yer istasyonu firmware'i
    ├── software_slave/                # STM32 robot (slave) firmware'i
    ├── Unity_codes/                   # Unity C# betikleri ve Blender modeli
    └── TarantulaRaspberryFinalCode.txt # Raspberry Pi QR/kamera uygulaması
```

## Sistem mimarisi

```text
Raspberry Pi kamera ──UART (115200)──> Yer istasyonu STM32 ──LoRa/UART (9600)──> Robot STM32
       │                                      │                                      │
       └── Flask canlı görüntü (:5000)        └──────── telemetri geri dönüşü ───────┘
                                                                              │
                                              GPS + BNO055 + PCA9685 + 12 servo

Unity dijital ikizi <── TCP (:7777) / komutlar ve `SERVO:` paketleri ──> Robot verisi
```

## Bileşenler

### Robot firmware'i

`Tarantula_codes/software_slave` STM32CubeIDE projesidir. Başlıca işlevleri:

- `USART2` üzerinden 115200 baud GPS/NMEA alımı.
- `USART6` üzerinden 9600 baud LoRa haberleşmesi.
- I2C ile BNO055 IMU ve PCA9685 servo sürücüsü kullanımı.
- `F`, `B`, `L`, `R`, `S` hareket komutları; `G` otonom navigasyon ve `A` iptal komutu.
- `$T:enlem,boylam` hedef paketi ile konum hedefi tanımlama.

Ana kaynak: `Tarantula_codes/software_slave/Core/Src/main.c`

### Yer istasyonu firmware'i

`Tarantula_codes/SoftwareProject` PC/Raspberry Pi ile LoRa hattı arasında paket köprüsü sağlar:

- `USART2`: PC tarafı, 115200 baud.
- `USART1`: LoRa tarafı, 9600 baud.
- `$` ile başlayan satır tabanlı paketleri hedef robot adresiyle iletir.

Ana kaynak: `Tarantula_codes/SoftwareProject/Core/Src/main.c`

### Raspberry Pi uygulaması

`TarantulaRaspberryFinalCode.txt`, OpenCV ve Pyzbar kullanarak kameradan QR kodları okur. Yeni kodlar `detected_qrs.txt` dosyasına yazılır ve `$QR:<veri>` biçiminde `/dev/serial0` üzerinden 115200 baud hızında STM32'ye gönderilir. Flask arayüzü varsayılan olarak `http://<raspberry-pi-ip>:5000` adresinde çalışır.

Gerekli Python paketleri:

```bash
pip install opencv-python pyzbar flask pyserial
```

Çalıştırmadan önce dosyayı `.py` uzantısıyla kaydedin ve kamera/UART erişiminin etkin olduğunu doğrulayın.

### Unity dijital ikizi

`Tarantula_codes/Unity_codes` içeriği Unity projenize eklenmek üzere hazırlanmıştır:

- `moving.cs`: TCP sunucusu (varsayılan port `7777`), klavye testi, robot hareketi ve 12 servo animasyonu.
- `IKUCampusEnvironment.cs`: İstanbul Kültür Üniversitesi kampüs ortamını oluşturur.
- `DroneCameraFollow.cs`: Drone/kamera takibi.
- `CameraHttpSnapshotStreamer.cs`: HTTP kamera görüntüsü entegrasyonu.
- `tarantula 1.blend`: Robot modeli.

Unity sahnesindeki `moving` bileşeninde `robotRoot` ve 12 servo `Transform` alanını atayın. TCP üzerinden hareket metinleri ile `SERVO:` önekli servo paketleri alınabilir.

## Kurulum ve kullanım

1. İlgili STM32 projesini STM32CubeIDE ile açın, kart/pin yapılandırmasını kendi donanımınıza göre doğrulayın ve derleyip karta yükleyin.
2. Robot tarafında GPS, BNO055, PCA9685, LoRa ve servo bağlantılarını firmware'deki UART/I2C ayarlarıyla eşleştirin.
3. Raspberry Pi'de gerekli paketleri yükleyip QR/kamera uygulamasını çalıştırın.
4. Unity betiklerini sahneye ekleyin; gerekiyorsa TCP istemcisini `7777` portuna bağlayın.
5. Önce servoları yüksüz ve güvenli bir test düzeneğinde deneyin; ardından hareket ve otonom navigasyonu kontrollü alanda doğrulayın.

## Dokümantasyon

- [Yazılım Gereksinimleri (SRS)](Tarantula_SRS.pdf)
- [IEEE Proje Raporu](Tarantula_IEEE.pdf)

## Notlar

- Pin bağlantıları ve donanım konfigürasyonu proje dosyalarında kart özelinde tanımlıdır; yükleme öncesinde mutlaka kontrol edilmelidir.
- Bu depo bir proje teslim arşividir; üçüncü taraf STM32 HAL sürücüleri ilgili STM32 lisans dosyalarına tabidir.
