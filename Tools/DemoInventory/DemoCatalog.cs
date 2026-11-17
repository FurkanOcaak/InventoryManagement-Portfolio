namespace InventoryManagement.DemoInventory;

public sealed record CatalogItem(string Name, decimal Price, int Minimum, string Group);

public static class DemoCatalog
{
    // Illustrative TRY prices, not vendor quotes or real GS1 allocations.
    public static readonly CatalogItem[] Products =
    [
        new("Cat6 UTP Ethernet Kablosu 305 m", 4250m, 8, "Ağ ekipmanları"),
        new("8 Port Gigabit Ethernet Switch", 1250m, 12, "Ağ ekipmanları"),
        new("24 Port Patch Panel", 1850m, 10, "Ağ ekipmanları"),
        new("500 GB NVMe SSD", 1950m, 15, "Bilgisayar bileşenleri"),
        new("16 GB DDR4 RAM", 1450m, 20, "Bilgisayar bileşenleri"),
        new("24 V DC Güç Kaynağı 5 A", 1150m, 10, "Elektrik malzemeleri"),
        new("RJ45 Konnektör 100'lü Paket", 450m, 25, "Ağ ekipmanları"),
        new("HDMI 2.0 Kablo 2 m", 225m, 30, "Çevre birimleri"),
        new("1 TB SATA SSD", 2850m, 12, "Bilgisayar bileşenleri"),
        new("650 W Bilgisayar Güç Kaynağı", 2350m, 8, "Bilgisayar bileşenleri"),
        new("120 mm Kasa Fanı", 275m, 25, "Bilgisayar bileşenleri"),
        new("İşlemci Termal Macunu 4 g", 185m, 20, "Bilgisayar bileşenleri"),
        new("Wi-Fi 6 Kablosuz Erişim Noktası", 3250m, 6, "Ağ ekipmanları"),
        new("SFP Gigabit Fiber Modül", 850m, 12, "Ağ ekipmanları"),
        new("Fiber Patch Kablo LC-LC 3 m", 350m, 20, "Ağ ekipmanları"),
        new("6'lı Topraklı Priz 3 m", 575m, 20, "Elektrik malzemeleri"),
        new("16 A Otomatik Sigorta", 195m, 25, "Elektrik malzemeleri"),
        new("2.5 mm² Kablo 100 m", 3750m, 6, "Elektrik malzemeleri"),
        new("Isı Daralan Makaron Seti", 165m, 20, "Elektrik malzemeleri"),
        new("Kablo Bağı 200 mm 100'lü", 95m, 40, "Elektrik malzemeleri"),
        new("Direnç Seti 600 Parça", 285m, 15, "Elektronik komponentler"),
        new("Kondansatör Seti 200 Parça", 350m, 15, "Elektronik komponentler"),
        new("Lehimsiz Deney Tahtası 830 Nokta", 175m, 20, "Elektronik komponentler"),
        new("5 V Röle Modülü 4 Kanal", 245m, 12, "Elektronik komponentler"),
        new("Dijital Multimetre", 850m, 8, "Elektronik komponentler"),
        new("Ayarlı Lehim İstasyonu", 2150m, 5, "Elektronik komponentler"),
        new("USB Kablolu Klavye", 425m, 18, "Ofis ekipmanları"),
        new("Optik USB Fare", 225m, 25, "Ofis ekipmanları"),
        new("24 İnç IPS Monitör", 4950m, 6, "Ofis ekipmanları"),
        new("A4 Fotokopi Kağıdı 500 Yaprak", 195m, 40, "Ofis ekipmanları"),
        new("Dizüstü Bilgisayar Standı", 650m, 10, "Ofis ekipmanları"),
        new("USB-C Çoklayıcı 6 Port", 1150m, 15, "Çevre birimleri"),
        new("128 GB USB Bellek", 375m, 25, "Depolama"),
        new("2 TB Harici Disk", 3250m, 8, "Depolama"),
        new("USB-C Kablo 1 m", 185m, 30, "Çevre birimleri"),
        new("1080p USB Web Kamera", 950m, 10, "Çevre birimleri"),
        new("DisplayPort Kablo 2 m", 325m, 20, "Çevre birimleri"),
        new("M.2 SSD Harici Kutu", 750m, 12, "Depolama"),
        new("USB Kulaklık", 850m, 10, "Ofis ekipmanları"),
        new("Kablo Düzenleyici Kanal 1 m", 125m, 20, "Elektrik malzemeleri")
    ];

    public static readonly (string Name, string Location)[] Warehouses =
    [
        ("Demo Elektronik Malzeme Deposu", "Sentetik tesis — E blok, raf E01-E12"),
        ("Demo Bilgisayar ve Ağ Deposu", "Sentetik tesis — B blok, raf B01-B16"),
        ("Demo Sevkiyat Deposu", "Sentetik tesis — S blok, paketleme alanı"),
        ("Demo Yedek Parça Deposu", "Sentetik tesis — Y blok, raf Y01-Y08"),
        ("Demo Merkez Depo", "Sentetik tesis — M blok, kabul alanı")
    ];
}
