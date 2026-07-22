# BMS UI — Formula Student HV BMS Masaüstü Arayüzü

Formula Student HV batarya master kartının (STM32G474 + FreeRTOS) USB CDC arayüzüne bağlanıp
96 hücrenin voltaj/sıcaklığını, paket verilerini, fault durumunu, balans durumunu ve kontaktör
çıkışlarını gösteren Windows uygulaması.

`lvbmsgui`'nin (LV BMS) HV karşılığıdır: aynı stack (C# .NET 10 WinForms + `System.IO.Ports`),
farklı protokol — LV ASCII satır gönderiyordu, HV **binary komut-cevap** konuşuyor.

## Gereksinimler

- Windows
- .NET 10 SDK (derlemek için) — çalıştırmak için .NET 10 Desktop Runtime yeterli
- Donanımsız çalıştırmak için **hiçbir şey gerekmez**: uygulama içi simülasyon modu var
  (aşağıya bakın). Harici `bms_simulator.py` yalnızca gerçek seri portu da sınamak
  isterseniz gerekir (Python 3 + `pyserial` + com0com/VSPE ile `COM10 ↔ COM11`).

## Derleme ve çalıştırma

```bash
dotnet build -c Release
dotnet run --project BmsUi
dotnet test
```

## Kullanım

1. Üstteki listeden COM portunu seçip **Başlat**'a basın (kart yoksa **Simülasyon** kutusunu
   işaretleyip Başlat demeniz yeterli). Liste kart takılıp çıkarıldığında **kendiliğinden
   tazelenir**; her port yanında türü yazar (`COM12 — USB`, `COM3 — Bluetooth`,
   `COM5 — ST-Link`), böylece hangisinin BMS olduğu bakınca anlaşılır.
2. Uygulama önce `0x17 0x71` ping'i gönderir; cihaz echo döndürmezse bağlanmaz
   (yanlış porta bağlanıp saçma veri göstermeyi engeller).
3. Bağlantı kurulunca poll worker başlar ve sol panel + sekmeler canlı güncellenir.

**Sol panel** (her sekmede görünür), yukarıdan aşağıya:

| Bölüm | İçerik |
|---|---|
| PAKET | Paket voltajı büyük punto + akım / güç / SoC / maks slave sıcaklığı kutuları |
| HÜCRELER | Min-maks-ortalama voltaj ve sıcaklık, hangi hücre olduğu (`#42`), **fark (maks−min)** ve **standart sapma** mV cinsinden — dengesizlik için tek bakışta okunacak sayılar |
| ÇIKIŞLAR | AIR / PRE / ERR durum hapları |
| HATALAR | Yalnızca **aktif** hatalar listelenir; hiçbiri yoksa yeşil "Aktif hata yok" |
| (alt şerit) | CRC / zaman aşımı / kimlik sayaçları, veri yaşı, bağlantı durumu |

Hata paneli bilerek yalnızca aktif olanları gösterir: 15 satırlık pasif liste ekranın yarısını
kaplayıp gerçek bir hatanın göze batmasını engelliyordu.

**Sekmeler:** Voltaj · Sıcaklık · Balans · Ayarlar · Log

> Uygulama BMS'e **hiçbir şey yazmaz** — yalnızca okur. Eşik/config yazma arayüzü bilinçli
> olarak yok; Ayarlar sekmesi sadece arayüzün görünümünü değiştirir.

## Hücre görünümü

Her hücre çerçevesiz dolu bir kutudur; yazı boyutu pencere boyutuyla ölçeklenir.

| Gösterim | Anlamı |
|---|---|
| Dolgu rengi | **Voltaj:** düşük = kırmızı, orta = sarı, yüksek = yeşil. **Sıcaklık:** düşük = koyu, yüksek = parlak amber (yüksek sıcaklık iyi bir şey olmadığı için yeşil kullanılmaz) |
| Ortadaki sayı | Değerin kendisi — renk yaklaşık, sayı kesin |
| Kalın kontur + **⚠** (sağ üst) | Değer alarm eşiklerinin dışında |
| **▲ / ▼** (sağ üst) | 96 hücrenin genel ortalamasının üstünde / altında |
| **σ+ / σ−** (sağ alt) | Kendi segmentinin ortalamasından ±1σ'dan fazla sapmış |
| **B** rozeti (sol alt) | Hücre balansta |
| Gri dolgu + "—" | Hücre geçersiz/stale (0.00 V) |
| Sol üstteki sayı | Lineer hücre indeksi — sol paneldeki min/maks indeksleriyle aynı numaralandırma |

Renk tek başına hiçbir şey taşımaz: değer her hücrede yazılı ve eşik dışı hücreler ayrıca ikon
alır. Bu, kırmızı-yeşil renk körlüğü (en yaygın tip) için de bilginin kaybolmadığı anlamına
gelir. Balans rozetinin konturu şart: altın renk, ramp'in sarı-turuncu ortasında dolguyla aynı
tona düşüp görünmez hâle geliyordu.

**Alarm dolguyu değiştirmez.** Voltaj skalasının düşük ucu zaten kırmızı olduğu için kırmızı
bir alarm dolgusu "düşük ama normal" hücreyle karışırdı; ayrıca eşik yanlış ayarlanınca bütün
ızgara tek renge düşer ve hiçbir hücre diğerinden ayırt edilemez hâle gelirdi. Alarm bunun
yerine kalın kontur + ⚠ ile gösterilir, dolgu değeri göstermeye devam eder.

**İki istatistik işareti neden ayrı?** ▲/▼ paketin bütününe göre konumu, σ± ise hücrenin kendi
komşularından ayrışıp ayrışmadığını söyler. Bir segment tümüyle paket ortalamasının altındaysa,
o segmentin en yüksek hücresi "σ+" ama yine de "▼" olabilir — ikisi farklı soruların cevabı.
Segment σ'sı 16 hücrenin tamamı üzerinden (popülasyon) hesaplanır. Paket geneli standart sapma
sol panelde mV cinsinden yazar.

## Ayarlar sekmesi (yalnızca görünüm)

Voltaj ve sıcaklık için ayrı ayrı:

- **Alarm alt/üst eşiği** — dışına çıkan hücre kalın kontur + uyarı ikonu alır
- **Renk skalası alt/üst ucu** — heatmap'in iki ucu. Paket dar bir aralıkta çalışırken
  (örn. 3.87-4.02 V) skalayı daraltmak hücreler arası farkı görünür kılar.

Varsayılanlar firmware eşikleriyle aynıdır (2.50 / 4.23 V, 80 °C — `main.h:194-200`), böylece
kutudan çıktığı hâliyle UI alarmı BMS fault'uyla örtüşür. Ayarlar
`%APPDATA%\BmsUi\settings.json` dosyasına kaydedilir, sonraki açılışta geri yüklenir.
Bu değerlerin **hiçbiri cihaza gönderilmez.**

## Log sekmesi (CSV)

Dosya seçip **Kaydı başlat** deyin; varsayılan 1 Hz, 0.1–10 Hz arası ayarlanabilir. Mevcut
dosyaya eklenir, başlık satırı tekrarlanmaz.

Sütun adları takımın **SD kart şablonuyla aynı** isimlendirmeyi kullanır
(`CAN Hattı ve SDCARD.xlsx` → SDCARD sayfası), böylece SD kart logları ile bu CSV aynı
araçlarla işlenebilir:

```
TIMESTAMP,
BMS_CELL0_VOLTAGE_f … BMS_CELL95_VOLTAGE_f,
BMS_CELL0_TEMP_f … BMS_CELL95_TEMP_f,
BMS_BALANCE_IC0_u16 … BMS_BALANCE_IC5_u16,
BMS_TOTAL_VOLTAGE_f, BMS_TOTAL_CELL_VOLTAGE_f, BMS_CURRENT_f, BMS_POWER_f,
BMS_ESTIMATED_SoC_f, BMS_FAULTS_u16, BMS_CONTRACTORS_u8,
BMS_MIN_CELL_VOLTAGE_f, BMS_MAX_CELL_VOLTAGE_f, BMS_AVG_CELL_VOLTAGE_f,
BMS_CELL_VOLTAGE_STDDEV_f, BMS_MIN_CELL_NUMBER_u8, BMS_MAX_CELL_NUMBER_u8,
BMS_MIN_CELL_TEMP_f, BMS_MAX_CELL_TEMP_f, BMS_AVG_CELL_TEMP_f, BMS_MAX_SLAVE_TEMP_f
```

216 sütun. Birimler: voltaj **V**, sıcaklık **°C**, akım **A**, güç **W**, SoC **%**,
`FAULTS`/`CONTRACTORS` bit maskesi, balans IC başına 16 bit.

`BMS_TOTAL_VOLTAGE_f` paket voltajı register'ı (idx 7), `BMS_TOTAL_CELL_VOLTAGE_f` ise 96
hücrenin firmware'deki toplamı (idx 11) — ikisi arasındaki küçük fark hücre başına yapılan
kırpmadan gelir ve ölçeklemenin doğruluğunu kontrol etmeye yarar.

Sayılar `InvariantCulture` ile yazılır (ondalık **nokta**), böylece dosya makineyle işlenirken
TR yerel ayarındaki virgül CSV'yi bozmaz. Arayüzdeki gösterim ise yerel ayara uyar.

## Uygulama içi simülasyon (kart gerekmez)

Bağlantı çubuğundaki **Simülasyon** kutusunu işaretleyip **Başlat**'a basın. Sürücü, sanal COM
portu veya Python gerekmez; istediğiniz an Durdur ile kapatırsınız.

Simülasyon, `ISerialTransport`'u gerçek portla aynı arayüzden uygulayan bir sanal cihazdır
([SimulatedTransport.cs](BmsUi/Serial/SimulatedTransport.cs)). Veri, gerçek kartla **birebir
aynı kod yolundan** akar — komut gönderimi, CRC8 doğrulaması, çerçeve ayrıştırma, poll
worker — sadece baytların kaynağı değişir. Ürettikleri:

- 96 hücre voltajı 3.30-4.19 V arasında sürüklenir (bir hücre belirgin min, biri belirgin maks)
- Sıcaklıklar 3 sıcak hücreyle birlikte sürüklenir; firmware'in `94 → 20` remap'i de taklit edilir
- Akım -120 … +80 A arasında salınır, SoC ortalama voltajdan türetilir
- 2. saniyede PRE, 5. saniyede AIR kapanır
- Fault paneli boş kalmasın diye 12 saniyede bir sırayla hücre OV / hücre aşırı sıcaklık /
  precharge zaman aşımı bitleri set edilir (ERR ışığı da onunla birlikte yanar)

## Harici simülatör (gerçek seri portu da sınamak için)

```bash
python bms_simulator.py --port COM11
```

Sonra uygulamada `COM10`'u seçip Başlat deyin.

| Bayrak | Ne yapar |
|---|---|
| `--port COM11` | Simülatörün tutacağı port |
| `--fault 2 --fault 13` | Verilen FAULTS bitlerini aktif eder |
| `--chunked` | Cevapları 64 baytlık parçalara böler (host birleştirme mantığını sınar) |
| `--latency 5` | Cevaba gecikme ekler (ms) |
| `--verbose` | Gelen her paketi yazar |

Simülatör firmware gibi davranır: gelen bayt öbeğinin **uzunluğuna** göre komutu ayrıştırır ve
her cevaba doğru CRC8 ekler.

## Protokol özeti

| Gönder | Anlam | Cevap |
|---|---|---|
| `0x29` (1 bayt) | 96 hücre voltaj | 194 bayt: 96×uint16 LE, `[192]=0x29`, `[193]=CRC8` |
| `0x2A` (1 bayt) | 96 hücre sıcaklık | 194 bayt: 96×**int16** LE (işaretli), `[192]=0x2A`, `[193]=CRC8` |
| `0x2B` (1 bayt) | Balans durumu | 14 bayt: 6×uint16 LE dcc bitmap, `[12]=0x2B`, `[13]=CRC8` |
| `idx` (idx<50, 1 bayt) | `MAINBUFFER[idx]` oku | 4 bayt: uint16 LE, `[2]=idx`, `[3]=CRC8` |
| `0x17 0x71` (2 bayt) | Ping | 2 bayt echo (CRC yok) |
| `idx,valLSB,valMSB` (3 bayt) | `MAINBUFFER[idx]=val` yaz | 4 bayt (oku ile aynı) |

- Hücre sırası: lineer `0..95 = segment*16 + cell` (6 segment × 16 hücre).
- Voltaj `raw/100` V · sıcaklık `(int16)raw/100` °C · `PACK_CURRENT` işaretli `raw/10` A ·
  SoC `raw/10000`.
- CRC8 = CRC-8/SMBUS (poly `0x07`, init `0x00`, refleksiyon yok), son bayt hariç tüm baytlar üzerinde.

### MAINBUFFER indeksleri

| idx | İsim | Ölçek |
|---|---|---|
| 0 | FAULTS | bit maskesi |
| 1 | OUTPUTS | bit0=AIR, bit1=PRE, bit2=ERR/SDC |
| 7 | PACK_VOLTAGE | ×100 V |
| 8 | PACK_CURRENT | işaretli ×10 A |
| 9 / 10 | MAX / MIN_CELL_VOLTAGE | ×100 V |
| 11 | TOTAL_CELL_VOLTAGE | ×100 V |
| 12 / 13 | MAX / MIN_CELL_TEMP | işaretli ×100 °C |
| 14 / 15 | AVG_CELL_VOLTAGE / _TEMP | ×100 |
| 16 | MAX_SLAVE_TEMP | işaretli ×100 |
| 17 | ESTIMATED_SoC | ×10000 |
| 30 | ALLOWED_DISBALANCE | mV (yazılabilir) |
| 32 / 33 | PRECHARGE_PERCENTAGE / _TIMEOUT | yazılabilir |

### FAULTS bitleri

0 PEC/haberleşme · 1 hücre UV · 2 hücre OV · 3 deşarj aşırı akım · 4 şarj aşırı akım ·
5 hücre düşük sıcaklık · 6 hücre aşırı sıcaklık · 7 hücre kopuk kablo · 8 akım sensörü yok ·
9 slave aşırı sıcaklık · 10 paket UV · 11 paket OV · 12 sıcaklık kopuk kablo ·
13 precharge zaman aşımı · 14 ölçüm bayat

## Mimari

```
Form1  ──Invoke──  PollWorker (arka plan thread, 10 Hz)
                        │
                   SerialLink  (komut-cevap, CRC + kimlik doğrulama, hata sayaçları)
                        │
                   ISerialTransport ──> SerialPortTransport (System.IO.Ports)
                                   └──> FakeTransport / FakeDeviceTransport (testler)
```

Poll planı (10 Hz temel tick, ~97 transaction/s):

| Veri | Hız |
|---|---|
| FAULTS, OUTPUTS, PACK_VOLTAGE, PACK_CURRENT | 10 Hz |
| min/max/avg/total/slave/SoC (idx 9-17) | 5 Hz |
| 96 voltaj + 96 sıcaklık | 5 Hz |
| Balans | 2 Hz |

Portu yalnızca worker thread'i kullanır; UI'den gelen yazma istekleri kuyruğa alınıp poll
turları arasında işlenir.

## Bilinen kısıtlar

- **Balans aç/kapa yapılamaz.** Firmware'de `BalanceEnable` MAINBUFFER'da değil, ayrı bir
  `volatile` global. `0x2B` komutu yalnızca durum okur. UI'den kontrol istenirse firmware'e
  yeni bir USB komutu eklenmeli.
- **Güç (kW) host'ta hesaplanır** (`V × I`). Firmware'de `MAINBUFFER[41]=POWER` var ama
  `41 = 0x29` voltaj komutuyla gölgelendiği için USB'den okunamaz. Aynı şekilde idx 42 ve 43
  de okunamaz.
- **Hücre 94'ün sıcaklığı kendi sensöründen gelmez.** Firmware
  `GUI_DATAS.Cell_Temps[94] = GUI_DATAS.Cell_Temps[20]` yapıyor (`main.cpp:971`) ve `0x2A`
  cevabı bu diziden okunuyor (`main.cpp:1976`) — yani 94. hücre hep 20. hücrenin sıcaklığını
  gösterir. Sıcaklık sekmesinde dipnot olarak belirtilir. (Voltajlar etkilenmez.)
- **SoC.** Plan aşamasında firmware `ESTIMATED_SoC`'u hesaplamıyordu (sabit 0). Gerçek kartla
  yapılan ilk denemede %73,7 okundu, yani artık hesaplanıyor — değerin doğruluğu firmware
  tarafında ayrıca doğrulanmalı.
- **UI cihaza yazmaz.** Eşik/config yazma arayüzü kaldırıldı; uygulama salt-okunur.
  Alt katmanda `SerialLink.WriteRegister` ve `PollWorker.EnqueueWrite` (test edilmiş olarak)
  duruyor, ileride gerekirse arayüz bunun üzerine eklenebilir.
- `idx >= 50` gönderilirse cihaz **hiç cevap vermez**; bu yüzden her transaction timeout'ludur.
- Komutlar tek tek gönderilir. Firmware paketi **uzunluğa göre** ayrıştırdığı için iki komut
  aynı USB paketinde birleşirse ping sanılır — bu yüzden aynı anda tek transaction kuralı vardır.

## Testler

`dotnet test` — 131 test:

- CRC-8/SMBUS bilinen vektörler (`"123456789"` → `0xF4`)
- 194/14/4 baytlık çerçeve ayrıştırma, işaretli sıcaklık ve akım, bozuk CRC ve yanlış kimlik reddi
- **Python ↔ C# çapraz doğrulama**: `bms_simulator.py`'nin ürettiği gerçek baytlar sabit
  olarak saklanıp C# ayrıştırıcısıyla çözülür
- Parçalı (chunked) gelen 194 baytın birleştirilmesi, zaman aşımı davranışı, hata sayaçları
- PollWorker uçtan uca: sahte cihaz → SerialLink → parser → snapshot, bağlantı kaybı, register yazma
- Uygulama içi simülasyon: ping, çerçevelerin gerçekçi aralıkta çözülmesi, `94 → 20` remap'i,
  `idx ≥ 50` için cevapsızlık, PollWorker ile uçtan uca sürülmesi
- UI duman testleri: ana pencere açılıp yerleşiyor mu, ızgara alarm/geçersiz/balans durumlarını
  çiziyor mu, AIR/PRE/ERR ışıkları yanıp sönüyor mu
- Renk: ramp'lerin monoton açıklığı (tek hue + monoton açıklık = CVD-güvenli), alarmın ramp
  adımı olmadığı, kullanıcı eşiklerinin firmware varsayılanlarını geçersiz kıldığı
- Görünüm ayarlarının diske yazılıp okunması, bozuk dosyada varsayılana dönmesi, ters
  aralıkların düzeltilmesi
- Çizim testleri PNG bırakır (`%TEMP%\bmsui_preview_*.png`) — ızgara, tüm pencere ve Ayarlar
  sekmesi; görsel kontrol için açıp bakabilirsiniz
