# BmsUi — Masaüstü UI Tasarımı (Spec)

Tarih: 2026-07-21
Durum: Uygulandı

## Revizyonlar (uygulama sırasında kararlaştırıldı)

- **Config paneli kaldırıldı.** UI artık BMS'e hiçbir şey yazmıyor, salt-okunur.
  Alt katmandaki `SerialLink.WriteRegister` / `PollWorker.EnqueueWrite` (test edilmiş olarak)
  duruyor; ileride arayüz bunun üzerine eklenebilir. `ALLOWED_DISBALANCE` (idx 30) yalnızca
  balans özetinde göstermek için okunuyor.
- **Uygulama içi simülasyon eklendi** (`BmsUi/Serial/SimulatedTransport.cs`). `ISerialTransport`
  gerçek portla aynı arayüzden uygulanır; sanal COM portu, sürücü veya Python gerekmez.
  Bağlantı çubuğundaki "Simulasyon" kutusuyla açılıp kapatılır.
- **Hücre 94 dipnotu düzeltildi.** Remap `GUI_DATAS.Cell_Temps[94] = Cell_Temps[20]`
  (`main.cpp:971`) ve yalnızca **sıcaklığı** etkiliyor; not Voltaj sekmesinden Sıcaklık
  sekmesine taşındı. (Aşağıdaki §4'te eski konumu yazıyordu.)

## 1. Amaç

Formula Student HV batarya master kartı (STM32G474 + FreeRTOS) USB'den (CDC sanal COM)
PC'ye takıldığında 96 hücrenin voltaj/sıcaklığını, paket verilerini, fault durumunu,
balans durumunu ve kontaktör çıkışlarını gösteren bir Windows masaüstü uygulaması.

Referans: `lvbmsgui` (LV BMS için C# .NET 10 WinForms + System.IO.Ports). Aynı stack ve
tarz korunur; protokol katmanı tamamen farklıdır (LV: ASCII satır push, HV: binary
komut-cevap).

Firmware kaynağı (protokolün doğrulandığı yer):
`workspace_1.19.0\bms_master_baremetal_freertos-claude-bms-freertos-migration-elqosd`
— `Core/Src/main.cpp` (`USB_Task`, `USBTransmit2bytes`, `calculateCRC8`),
`Core/Inc/main.h` (MAINBUFFER indeksleri, eşikler).

## 2. Protokol (firmware'den doğrulandı)

Taşıma: USB CDC sanal COM, 115200 (CDC'de baud önemsiz). Host komut yollar, cihaz
sabit uzunlukta binary cevap döner.

| Gönder | Anlam | Cevap |
|---|---|---|
| `0x29` (41), 1 bayt | 96 hücre voltaj | 194 bayt: 96×uint16 LE @[0..191], `[192]=0x29`, `[193]=CRC8([0..192])` |
| `0x2A` (42), 1 bayt | 96 hücre sıcaklık | 194 bayt: 96×**int16** LE (işaretli), `[192]=0x2A`, `[193]=CRC8` |
| `0x2B` (43), 1 bayt | balans durumu | 14 bayt: 6×uint16 LE dcc bitmap, `[12]=0x2B`, `[13]=CRC8([0..12])` |
| `idx` (idx<50), 1 bayt | `MAINBUFFER[idx]` oku | 4 bayt: uint16 LE @[0..1], `[2]=idx`, `[3]=CRC8([0..2])` |
| `0x17 0x71`, 2 bayt | ping | 2 bayt echo (CRC yok) |
| `idx,valLSB,valMSB`, 3 bayt | `MAINBUFFER[idx]=val` yaz | 4 bayt (oku ile aynı format) |

Ölçekler: voltaj `raw/100` V · sıcaklık `(int16)raw/100` °C · `PACK_CURRENT` işaretli
`raw/10` A · `SoC` `raw/10000` · `PACK_MAX/MIN_CELL_TEMP` ve `PACK_MAX_SLAVE_TEMP`
işaretli ×100.

Hücre sırası: lineer `0..95 = segment*16 + cell`, 6 segment × 16 hücre.

CRC8: CRC-8/SMBUS — poly `0x07`, init `0x00`, refleksiyon yok, xorout `0x00`, son bayt
hariç tüm baytlar üzerinde (`main.cpp:2119` ile birebir).

### 2.1 Firmware'den çıkan üç kritik kısıt

1. **Uzunluğa göre ayrıştırma** (`main.cpp:1958` `switch (usblen)`). `usblen` tek bir CDC
   RX callback'inde gelen bayt sayısıdır. Host iki 1-baytlık komutu arka arkaya yazarsa
   ve bunlar tek USB paketinde birleşirse cihaz bunu ping (len=2) sanar. **Sonuç:** aynı
   anda tek transaction; komut tek `Write` çağrısıyla yazılır, cevap tam okunmadan yeni
   komut gönderilmez.
2. **idx 41/42/43 gölgelenmiş.** `main.h:123` `#define POWER 41` var ama len=1 iken 41/42/43
   özel komut olarak yakalanıyor, MAINBUFFER'dan okunamıyor. **Sonuç:** güç host'ta
   `PACK_VOLTAGE × PACK_CURRENT` ile hesaplanır.
3. **`idx >= 50` sessizce düşürülüyor** (`main.cpp:1998`, cevap yok). **Sonuç:** her
   transaction'ın timeout'u zorunlu.

### 2.2 MAINBUFFER indeksleri (`main.h:93-123`)

| idx | isim | ölçek / anlam |
|---|---|---|
| 0 | FAULTS | bit maskesi |
| 1 | OUTPUTS | bit0=AIR, bit1=PRE, bit2=ERR/SDC |
| 7 | PACK_VOLTAGE | ×100 V |
| 8 | PACK_CURRENT | işaretli ×10 A |
| 9 / 10 | PACK_MAX / MIN_CELL_VOLTAGE | ×100 V |
| 11 | PACK_TOTAL_CELL_VOLTAGE | ×100 V |
| 12 / 13 | PACK_MAX / MIN_CELL_TEMP | işaretli ×100 °C |
| 14 / 15 | PACK_AVG_CELL_VOLTAGE / _TEMP | ×100 |
| 16 | PACK_MAX_SLAVE_TEMP | işaretli ×100 |
| 17 | ESTIMATED_SoC | ×10000 (firmware'de şu an 0) |
| 30 | ALLOWED_DISBALANCE | mV, yazılabilir |
| 32 / 33 | PRECHARGE_PERCENTAGE / _TIMEOUT | yazılabilir |

FAULTS bitleri: 0 PEC/comms · 1 cellUV · 2 cellOV · 3 disOC · 4 chgOC · 5 cellUnderT ·
6 cellOverT · 7 cellOpenWire · 8 noCurrentSensor · 9 slaveOverT · 10 packUV · 11 packOV ·
12 tempOpenWire · 13 prechargeTO · 14 measStale.

Firmware eşikleri (heatmap sınırları için, `main.h:194-200`): hücre UV 2.5 V, hücre OV
4.23 V, hücre aşırı sıcaklık 80 °C, slave aşırı sıcaklık 80 °C.

## 3. Mimari

```
workspace_1.19.0\bmsui\
├── BmsUi.sln
├── README.md
├── bms_simulator.py
├── BmsUi\                              (net10.0-windows, WinForms)
│   ├── Program.cs
│   ├── Form1.cs / Form1.Designer.cs        UI + Invoke
│   ├── Protocol\Crc8.cs                    CRC-8/SMBUS
│   ├── Protocol\HvProtocol.cs              komut/uzunluk sabitleri, MAINBUFFER idx, fault bitleri
│   ├── Protocol\FrameParser.cs             194/14/4 bayt → tipli sonuç, CRC + id doğrulama
│   ├── Serial\ISerialTransport.cs          test edilebilirlik arayüzü
│   ├── Serial\SerialPortTransport.cs       System.IO.Ports sarmalayıcı
│   ├── Serial\SerialLink.cs                Open/Close/Ping/Transact
│   ├── Model\BmsSnapshot.cs                değişmez tam durum
│   ├── Polling\PollWorker.cs               arka plan thread + yazma kuyruğu
│   ├── Ui\CellGridControl.cs               owner-drawn 6×16 ızgara
│   └── Logging\CsvLogger.cs
└── BmsUi.Tests\                        (xUnit)
```

lvbmsgui'den korunanlar: `SerialPort.GetPortNames()` → comboBox, Start/Stop butonu
(`Open()`/`Close()`), `this.Invoke(...)` ile UI-thread güncelleme, `dotnet build -c Release`,
README.

`SerialLink` doğrudan `SerialPort` yerine `ISerialTransport` üzerine kurulur; böylece
parçalı paket ve timeout senaryoları gerçek COM portu olmadan test edilir.

### 3.1 Transaction disiplini

`Transact(cmd, expectedLen, expectedId)`:

1. `DiscardInBuffer()` — önceki timeout'tan kalan artık baytlar akışı zehirler
2. komutu tek `Write` çağrısıyla yaz
3. `expectedLen` bayta kadar biriktir (`ReadTimeout` 200 ms, toplam deadline 300 ms)
4. `buf[N-2] == expectedId` **ve** `Crc8(buf, N-1) == buf[N-1]` doğrula
5. hata → sayaç++, `DiscardInBuffer()`, `null` dön

Ping istisna: 2 bayt echo, CRC yok, id yok.

Portu yalnızca PollWorker thread'i kullanır. UI'den gelen MAINBUFFER yazma istekleri
`ConcurrentQueue`'ya girer, worker poll turları arasında işler — iki thread aynı porta
yazmaz.

### 3.2 Poll planı (10 Hz temel tick)

| Veri | Hız | Komut | Transaction/s |
|---|---|---|---|
| FAULTS, OUTPUTS, PACK_VOLTAGE, PACK_CURRENT (idx 0,1,7,8) | 10 Hz | 4× 4-bayt | 40 |
| min/max/avg/total/slave/SoC (idx 9-17) | 5 Hz | 9× 4-bayt | 45 |
| 96 voltaj (0x29) + 96 sıcaklık (0x2A) | 5 Hz | 2× 194-bayt | 10 |
| Balans (0x2B) | 2 Hz | 1× 14-bayt | 2 |

Toplam ~97 transaction/s.

min/max hücrenin **indeksi** firmware'de yok; host 0x29/0x2A dizilerinden hesaplar.
MAINBUFFER 9-16 değerleri yanında "firmware raporu" olarak gösterilir (çapraz kontrol).

Worker her 10 Hz turda değişmez bir `BmsSnapshot` üretip UI'ye `BeginInvoke` ile gönderir.
Snapshot her zaman **tam** durumu taşır: o turda yenilenmeyen alanlar (örn. 5 Hz'lik hücre
dizileri) son bilinen değerleriyle kopyalanır ve her alan grubunun kendi "son güncelleme
zamanı" damgası bulunur — UI veri yaşını buradan gösterir. UI meşgulse kuyruk şişmesin diye
"UI güncelleme devam ediyor" bayrağıyla ara turlar atlanır.

## 4. UI

`SplitContainer`: sol sabit panel (her sekmede görünür) + sağ `TabControl`.

**Sol panel:** COM combo + Start/Stop + bağlantı durumu · paket V / A / kW / SoC ·
min/max/avg V ve T + hücre indeksi · AIR/PRE/ERR ışıkları · 15 fault satırı (aktif kırmızı) ·
CRC/timeout hata sayaçları + veri yaşı (ms).

**Sekmeler:** Voltaj · Sıcaklık · Balans · Config · Log.

`CellGridControl`: owner-drawn, `DoubleBuffered`, tek `Paint` (96 ayrı Label yok).
Hücre başına `S1-C03` etiketi, değer, heatmap dolgu.

Heatmap kuralı — renk **normal çalışma bandında** gradyan, banttan çıkınca alarm rengi:

- Voltaj: 3.2 V (mavi) → 4.10 V (yeşil) gradyanı. `< 2.5 V` (UV) veya `> 4.23 V` (OV)
  → düz kırmızı + kalın çerçeve. `0.00 V` → gri (geçersiz/stale).
- Sıcaklık: 15 °C (mavi) → 60 °C (turuncu) gradyanı. `> 80 °C` (firmware eşiği) → düz
  kırmızı + kalın çerçeve. Negatif değerler mavi uçta gösterilir (int16 işaretli).

Balanstaki hücreler voltaj sekmesinde de sarı çerçeveli.

**Config sekmesi:** ALLOWED_DISBALANCE (30), PRECHARGE_PERCENTAGE (32), PRECHARGE_TIMEOUT (33)
oku/yaz. Yazma öncesi onay dialogu (canlı BMS davranışını değiştirir), yazma sonrası cihazın
echo'suyla doğrulama. Yazılan değerler firmware'de RAM'deki MAINBUFFER'a gider; kalıcılık
firmware'in flash davranışına bağlıdır, UI bunu "geçici (RAM)" olarak etiketler.

**Log sekmesi:** CSV kaydı aç/kapa, dosya yolu, kayıt hızı (varsayılan 1 Hz). Satır:
zaman damgası, 96 voltaj, 96 sıcaklık, paket alanları, FAULTS, OUTPUTS, balans bitmap'leri.

Dil: UI metinleri Türkçe, kod tanımlayıcıları İngilizce, yorumlar Türkçe.

Not: firmware'de `cellTemps[94] = cellTemps[20]` remap'i var (`main.cpp:2045`); 94. hücre
20. ile aynı görünür. UI'de dipnot olarak belirtilir.

## 5. Hata yönetimi

- Bağlanınca önce `0x17 0x71` ping; cevap yoksa port kapatılır, kullanıcıya "cihaz cevap
  vermiyor" bildirilir.
- Çalışırken üst üste 5 transaction hatası → "bağlantı kayıp" durumu, worker durur, son
  değerler soluk gösterilir.
- Her transaction timeout'lu; `idx >= 50` gibi cevapsız durumlarda thread kilitlenmez.
- Port kapanınca worker temiz şekilde durur (`CancellationToken` + `Join`).
- CRC hatası, timeout ve id uyuşmazlığı ayrı sayaçlarda tutulup UI'de gösterilir.

## 6. Simülatör

`bms_simulator.py` (pyserial), sanal port çifti (VSPE / com0com, COM10↔COM11):

- Gelen bayt öbeğini firmware gibi **uzunluğa göre** ayrıştırır.
- `0x29` → 194 bayt sürüklenen voltaj (3.6-4.2 V), `0x2A` → 194 bayt sıcaklık (birkaç sıcak
  hücre), `0x2B` → 14 bayt balans bitmap (ortalamanın üstündeki hücreler), `idx` → 4 bayt
  MAINBUFFER, `0x17 0x71` → echo. Her cevapta doğru CRC8.
- AIR/PRE sekansı ve SoC/akım simülasyonu.
- Bayraklar: `--port COM11`, `--fault <bit>` (fault enjekte), `--chunked` (cevabı parçalara
  bölerek gönderir → host birleştirme mantığını test eder), `--latency <ms>`.

## 7. Test

xUnit projesi (`dotnet test`):

- CRC8 bilinen vektörler (`"123456789"` → `0xF4`) ve firmware algoritmasıyla üretilmiş
  194-bayt örnek çerçeve.
- 194-bayt voltaj parse, 194-bayt sıcaklık parse **negatif değerlerle** (int16).
- 4-bayt MAINBUFFER parse; `PACK_CURRENT` negatif (işaretli ×10).
- 14-bayt balans bitmap → hücre indeksleri.
- Bozuk CRC reddi, yanlış id reddi.
- Parçalı gelen 194 baytın birleştirilmesi (fake transport, 3 parça).
- Timeout davranışı (eksik cevap → `null`, sayaç artışı).

## 8. Fazlar

| Faz | İçerik |
|---|---|
| F1 | WinForms iskelet + seri: COM combo, Start/Stop, `SerialLink.Open/Close`, ping doğrulama |
| F2 | Protokol + simülatör: `Crc8`, `Transact`, 0x29/0x2A/0x2B/MAINBUFFER parse, xUnit testleri, `bms_simulator.py` ile uçtan uca |
| F3 | Model + poll worker: katmanlı 10/5/2 Hz thread, `BmsSnapshot`, `Invoke` ile UI |
| F4 | 96-hücre ızgarası: voltaj heatmap + sıcaklık sekmesi |
| F5 | Paket paneli (V/A/kW/SoC/min-max-avg + indeks) + fault decode + AIR/PRE/ERR |
| F6 | Balans görünümü + config paneli (idx 30/32/33 yazma) |
| F7 | CSV loglama + cila: yeniden bağlanma, hata sayaçları, tema |

## 9. Kapsam dışı (YAGNI)

- Balans aç/kapa kontrolü: `BalanceEnable` firmware'de MAINBUFFER'da değil (ayrı `volatile`
  global); cmd `0x2B` yalnızca durum okur. UI'den kontrol istenirse **firmware'e yeni USB
  komutu** eklenmeli — bu spec'in kapsamı dışında.
- Şarj cihazı kontrolü (MAINBUFFER 2-6, 31), CAN üzerinden veri, geçmiş grafik/trend
  çizimi, çoklu cihaz desteği.
