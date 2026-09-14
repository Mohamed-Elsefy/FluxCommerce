-- FluxCommerce :: Catalog seed (Products + Categories)
-- Run in SSMS against the Catalog database.
-- Database per appsettings.Development.json: FluxCommerce_Catalog
-- Idempotent: safe to run multiple times (guards on unique Slug / Sku).

USE [FluxCommerce_Catalog];
GO

SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

-- ============================================================
-- 1) Categories
--    Status: 0 = Active, 1 = Inactive
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'electronics')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '11111111-1111-1111-1111-111111111111',
        N'Electronics',
        N'electronics',
        N'All electronic devices and gadgets.',
        NULL,
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'smartphones')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '22222222-2222-2222-2222-222222222222',
        N'Smartphones',
        N'smartphones',
        N'Mobile phones and accessories.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'laptops')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '33333333-3333-3333-3333-333333333333',
        N'Laptops',
        N'laptops',
        N'Notebooks and ultrabooks.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'accessories')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '44444444-4444-4444-4444-444444444444',
        N'Accessories',
        N'accessories',
        N'Chargers, audio and bags.',
        NULL,
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'tablets')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '55555555-5555-5555-5555-555555555555',
        N'Tablets',
        N'tablets',
        N'Tablets and e-readers.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'wearables')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '66666666-6666-6666-6666-666666666666',
        N'Wearables',
        N'wearables',
        N'Smartwatches and fitness bands.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'audio')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '77777777-7777-7777-7777-777777777777',
        N'Audio',
        N'audio',
        N'Headphones, earbuds and speakers.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'gaming')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '88888888-8888-8888-8888-888888888888',
        N'Gaming',
        N'gaming',
        N'Keyboards, mice and controllers.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'home-office')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        '99999999-9999-9999-9999-999999999999',
        N'Home Office',
        N'home-office',
        N'Monitors, docks, webcams and hubs.',
        NULL,
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'cables-hubs')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
        N'Cables and Hubs',
        N'cables-hubs',
        N'USB-C hubs, cables and adapters.',
        '44444444-4444-4444-4444-444444444444',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

-- ============================================================
-- 2) Products
--    Status: 0 = Draft, 1 = Active | Visibility: 0 = Visible
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'nova-x5-smartphone')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A1111111-1111-1111-1111-111111111111',
        N'Nova X5 Smartphone',
        N'nova-x5-smartphone',
        N'6.5 inch display, 128GB storage, dual camera test device.',
        '22222222-2222-2222-2222-222222222222',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'nova-laptop-pro-14')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A2222222-2222-2222-2222-222222222222',
        N'Nova Laptop Pro 14',
        N'nova-laptop-pro-14',
        N'14 inch laptop, 16GB RAM, 512GB SSD test device.',
        '33333333-3333-3333-3333-333333333333',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'aero-wireless-headphones')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A3333333-3333-3333-3333-333333333333',
        N'Aero Wireless Headphones',
        N'aero-wireless-headphones',
        N'Bluetooth over-ear headphones for testing cart and checkout.',
        '77777777-7777-7777-7777-777777777777',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'volt-usb-c-charger-65w')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A4444444-4444-4444-4444-444444444444',
        N'Volt USB-C Charger 65W',
        N'volt-usb-c-charger-65w',
        N'65W fast charger for testing low-price orders.',
        '44444444-4444-4444-4444-444444444444',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'trek-laptop-backpack')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A5555555-5555-5555-5555-555555555555',
        N'Trek Laptop Backpack',
        N'trek-laptop-backpack',
        N'Water-resistant 15 inch laptop backpack.',
        '44444444-4444-4444-4444-444444444444',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'pulse-tab-11')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A6666666-6666-6666-6666-666666666666',
        N'Pulse Tab 11',
        N'pulse-tab-11',
        N'11 inch tablet, 8GB RAM, 128GB storage, stylus support.',
        '55555555-5555-5555-5555-555555555555',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'orbit-smartwatch-s2')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A7777777-7777-7777-7777-777777777777',
        N'Orbit Smartwatch S2',
        N'orbit-smartwatch-s2',
        N'AMOLED smartwatch with heart-rate and GPS.',
        '66666666-6666-6666-6666-666666666666',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'bass-boom-speaker')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A8888888-8888-8888-8888-888888888888',
        N'Bass Boom Speaker',
        N'bass-boom-speaker',
        N'Portable Bluetooth speaker, 20h battery, waterproof IPX7.',
        '77777777-7777-7777-7777-777777777777',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'strike-gaming-mouse')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'A9999999-9999-9999-9999-999999999999',
        N'Strike Gaming Mouse',
        N'strike-gaming-mouse',
        N'26K DPI wireless gaming mouse, 90h battery.',
        '88888888-8888-8888-8888-888888888888',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'strike-gaming-keyboard')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'AAAAAAAA-1111-1111-1111-111111111111',
        N'Strike Gaming Keyboard',
        N'strike-gaming-keyboard',
        N'Mechanical TKL keyboard, hot-swap, RGB.',
        '88888888-8888-8888-8888-888888888888',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'vision-27-monitor-4k')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'AAAAAAAA-2222-2222-2222-222222222222',
        N'Vision 27 Monitor 4K',
        N'vision-27-monitor-4k',
        N'27 inch 4K IPS monitor, 144Hz, HDR400 for home office.',
        '99999999-9999-9999-9999-999999999999',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'link-usb-c-hub-7in1')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'AAAAAAAA-3333-3333-3333-333333333333',
        N'Link USB-C Hub 7in1',
        N'link-usb-c-hub-7in1',
        N'7-in-1 USB-C hub with HDMI 4K, PD 100W and SD reader.',
        'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'focus-webcam-4k')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'AAAAAAAA-4444-4444-4444-444444444444',
        N'Focus Webcam 4K',
        N'focus-webcam-4k',
        N'4K webcam with autofocus and noise-cancelling mic.',
        '99999999-9999-9999-9999-999999999999',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'quiet-true-wireless-earbuds')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'AAAAAAAA-5555-5555-5555-555555555555',
        N'Quiet True Wireless Earbuds',
        N'quiet-true-wireless-earbuds',
        N'ANC earbuds, 30h case battery, wireless charging.',
        '77777777-7777-7777-7777-777777777777',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'nova-x5-lite')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'AAAAAAAA-6666-6666-6666-666666666666',
        N'Nova X5 Lite',
        N'nova-x5-lite',
        N'Budget 6.1 inch phone for testing multi-variant catalog.',
        '22222222-2222-2222-2222-222222222222',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

-- ============================================================
-- 3) Variants (owned: Sku unique, UnitPriceAmount decimal(18,6))
--    Status: 0 = Active
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'NOVA-X5-128-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B1111111-1111-1111-1111-111111111111',
        'A1111111-1111-1111-1111-111111111111',
        N'NOVA-X5-128-BLK',
        N'Nova X5 / 128GB / Black',
        499.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'NOVA-X5-256-WHT')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B1111111-1111-1111-1111-111111111112',
        'A1111111-1111-1111-1111-111111111111',
        N'NOVA-X5-256-WHT',
        N'Nova X5 / 256GB / White',
        599.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'NOVA-LP14-16-512')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B2222222-2222-2222-2222-222222222222',
        'A2222222-2222-2222-2222-222222222222',
        N'NOVA-LP14-16-512',
        N'Laptop Pro 14 / 16GB / 512GB',
        1299.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'NOVA-LP14-32-1TB')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B2222222-2222-2222-2222-222222222223',
        'A2222222-2222-2222-2222-222222222222',
        N'NOVA-LP14-32-1TB',
        N'Laptop Pro 14 / 32GB / 1TB',
        1699.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'AERO-HP-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B3333333-3333-3333-3333-333333333333',
        'A3333333-3333-3333-3333-333333333333',
        N'AERO-HP-BLK',
        N'Aero Headphones / Black',
        149.500000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'AERO-HP-WHT')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B3333333-3333-3333-3333-333333333334',
        'A3333333-3333-3333-3333-333333333333',
        N'AERO-HP-WHT',
        N'Aero Headphones / White',
        149.500000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'VOLT-C65W')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B4444444-4444-4444-4444-444444444444',
        'A4444444-4444-4444-4444-444444444444',
        N'VOLT-C65W',
        N'Volt Charger 65W',
        39.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'TREK-BP15-GRY')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B5555555-5555-5555-5555-555555555555',
        'A5555555-5555-5555-5555-555555555555',
        N'TREK-BP15-GRY',
        N'Trek Backpack 15in / Grey',
        79.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'TREK-BP15-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B5555555-5555-5555-5555-555555555556',
        'A5555555-5555-5555-5555-555555555555',
        N'TREK-BP15-BLK',
        N'Trek Backpack 15in / Black',
        79.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'PULSE-T11-128-GRY')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B6666666-6666-6666-6666-666666666666',
        'A6666666-6666-6666-6666-666666666666',
        N'PULSE-T11-128-GRY',
        N'Pulse Tab 11 / 128GB / Grey',
        349.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'PULSE-T11-256-SLV')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B6666666-6666-6666-6666-666666666667',
        'A6666666-6666-6666-6666-666666666666',
        N'PULSE-T11-256-SLV',
        N'Pulse Tab 11 / 256GB / Silver',
        429.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'ORBIT-S2-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B7777777-7777-7777-7777-777777777777',
        'A7777777-7777-7777-7777-777777777777',
        N'ORBIT-S2-BLK',
        N'Orbit S2 / Black / Sport band',
        249.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'ORBIT-S2-SLV')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B7777777-7777-7777-7777-777777777778',
        'A7777777-7777-7777-7777-777777777777',
        N'ORBIT-S2-SLV',
        N'Orbit S2 / Silver / Leather band',
        299.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'BASS-BOOM-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B8888888-8888-8888-8888-888888888888',
        'A8888888-8888-8888-8888-888888888888',
        N'BASS-BOOM-BLK',
        N'Bass Boom / Black',
        119.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'STRIKE-MOUSE-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'B9999999-9999-9999-9999-999999999999',
        'A9999999-9999-9999-9999-999999999999',
        N'STRIKE-MOUSE-BLK',
        N'Strike Mouse / Black',
        89.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'STRIKE-KB-TKL')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'BBBBBBBB-1111-1111-1111-111111111111',
        'AAAAAAAA-1111-1111-1111-111111111111',
        N'STRIKE-KB-TKL',
        N'Strike Keyboard TKL / Brown switch',
        129.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'VISION27-4K')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'BBBBBBBB-2222-2222-2222-222222222222',
        'AAAAAAAA-2222-2222-2222-222222222222',
        N'VISION27-4K',
        N'Vision 27 4K / Black',
        549.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'LINK-HUB-7IN1')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'BBBBBBBB-3333-3333-3333-333333333333',
        'AAAAAAAA-3333-3333-3333-333333333333',
        N'LINK-HUB-7IN1',
        N'Link Hub 7in1 / Grey',
        59.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'FOCUS-CAM-4K')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'BBBBBBBB-4444-4444-4444-444444444444',
        'AAAAAAAA-4444-4444-4444-444444444444',
        N'FOCUS-CAM-4K',
        N'Focus Webcam 4K',
        179.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'QUIET-TWS-WHT')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'BBBBBBBB-5555-5555-5555-555555555555',
        'AAAAAAAA-5555-5555-5555-555555555555',
        N'QUIET-TWS-WHT',
        N'Quiet Earbuds / White',
        99.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'QUIET-TWS-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'BBBBBBBB-5555-5555-5555-555555555556',
        'AAAAAAAA-5555-5555-5555-555555555555',
        N'QUIET-TWS-BLK',
        N'Quiet Earbuds / Black',
        99.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'NOVA-LITE-64-BLU')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'BBBBBBBB-6666-6666-6666-666666666666',
        'AAAAAAAA-6666-6666-6666-666666666666',
        N'NOVA-LITE-64-BLU',
        N'Nova Lite / 64GB / Blue',
        199.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'NOVA-LITE-128-BLU')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'BBBBBBBB-6666-6666-6666-666666666667',
        'AAAAAAAA-6666-6666-6666-666666666666',
        N'NOVA-LITE-128-BLU',
        N'Nova Lite / 128GB / Blue',
        249.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

-- ============================================================
-- 4) ProductImages
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/nova-x5/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'C1111111-1111-1111-1111-111111111111',
        'A1111111-1111-1111-1111-111111111111',
        N'https://picsum.photos/seed/nova-x5/800/600',
        N'Nova X5 front view',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/nova-lp14/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'C2222222-2222-2222-2222-222222222222',
        'A2222222-2222-2222-2222-222222222222',
        N'https://picsum.photos/seed/nova-lp14/800/600',
        N'Laptop Pro 14 open view',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/aero-hp/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'C3333333-3333-3333-3333-333333333333',
        'A3333333-3333-3333-3333-333333333333',
        N'https://picsum.photos/seed/aero-hp/800/600',
        N'Aero headphones',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/pulse-t11/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'C6666666-6666-6666-6666-666666666666',
        'A6666666-6666-6666-6666-666666666666',
        N'https://picsum.photos/seed/pulse-t11/800/600',
        N'Pulse Tab 11',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/orbit-s2/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'C7777777-7777-7777-7777-777777777777',
        'A7777777-7777-7777-7777-777777777777',
        N'https://picsum.photos/seed/orbit-s2/800/600',
        N'Orbit Smartwatch S2',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/bass-boom/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'C8888888-8888-8888-8888-888888888888',
        'A8888888-8888-8888-8888-888888888888',
        N'https://picsum.photos/seed/bass-boom/800/600',
        N'Bass Boom speaker',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/strike-mouse/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'C9999999-9999-9999-9999-999999999999',
        'A9999999-9999-9999-9999-999999999999',
        N'https://picsum.photos/seed/strike-mouse/800/600',
        N'Strike gaming mouse',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/vision27/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'CCCCCCCC-2222-2222-2222-222222222222',
        'AAAAAAAA-2222-2222-2222-222222222222',
        N'https://picsum.photos/seed/vision27/800/600',
        N'Vision 27 monitor',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/quiet-tws/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'CCCCCCCC-5555-5555-5555-555555555555',
        'AAAAAAAA-5555-5555-5555-555555555555',
        N'https://picsum.photos/seed/quiet-tws/800/600',
        N'Quiet earbuds',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

-- ============================================================
-- 5) ProductSpecifications
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'A1111111-1111-1111-1111-111111111111' AND Name = N'Storage')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('D1111111-1111-1111-1111-111111111111', 'A1111111-1111-1111-1111-111111111111', N'Storage', N'128GB / 256GB', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'A2222222-2222-2222-2222-222222222222' AND Name = N'RAM')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('D2222222-2222-2222-2222-222222222222', 'A2222222-2222-2222-2222-222222222222', N'RAM', N'16GB', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'A6666666-6666-6666-6666-666666666666' AND Name = N'Display')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('D6666666-6666-6666-6666-666666666666', 'A6666666-6666-6666-6666-666666666666', N'Display', N'11 inch 120Hz', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'A7777777-7777-7777-7777-777777777777' AND Name = N'Battery')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('D7777777-7777-7777-7777-777777777777', 'A7777777-7777-7777-7777-777777777777', N'Battery', N'14 days typical', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'A8888888-8888-8888-8888-888888888888' AND Name = N'Battery')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('D8888888-8888-8888-8888-888888888888', 'A8888888-8888-8888-8888-888888888888', N'Battery', N'20h playback', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'A9999999-9999-9999-9999-999999999999' AND Name = N'Sensor')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('D9999999-9999-9999-9999-999999999999', 'A9999999-9999-9999-9999-999999999999', N'Sensor', N'26K DPI optical', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'AAAAAAAA-2222-2222-2222-222222222222' AND Name = N'Resolution')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('DDDDDDDD-2222-2222-2222-222222222222', 'AAAAAAAA-2222-2222-2222-222222222222', N'Resolution', N'3840x2160 144Hz', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'AAAAAAAA-5555-5555-5555-555555555555' AND Name = N'ANC')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('DDDDDDDD-5555-5555-5555-555555555555', 'AAAAAAAA-5555-5555-5555-555555555555', N'ANC', N'Hybrid active noise cancellation', SYSUTCDATETIME());
END
GO

-- ============================================================
-- 6) EXTRA Categories (batch 2: smart-home, cameras, drones, storage)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'smart-home')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E0000001-0001-0001-0001-000000000001',
        N'Smart Home',
        N'smart-home',
        N'Speakers, hubs and automation.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'cameras')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E0000002-0002-0002-0002-000000000002',
        N'Cameras',
        N'cameras',
        N'Mirrorless, action and security cameras.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'drones')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E0000003-0003-0003-0003-000000000003',
        N'Drones',
        N'drones',
        N'Camera drones and accessories.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Slug = N'storage')
BEGIN
    INSERT INTO dbo.Categories (Id, Name, Slug, Description, ParentId, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E0000004-0004-0004-0004-000000000004',
        N'Storage',
        N'storage',
        N'SSD, HDD and memory cards.',
        '11111111-1111-1111-1111-111111111111',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

-- ============================================================
-- 7) EXTRA Products (batch 2: 15 products, all Active/Visible)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'echo-smart-speaker-mini')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000001-0001-0001-0001-000000000001',
        N'Echo Smart Speaker Mini',
        N'echo-smart-speaker-mini',
        N'Compact smart speaker with voice assistant.',
        'E0000001-0001-0001-0001-000000000001',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'sentinel-cam-360')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000002-0002-0002-0002-000000000002',
        N'Sentinel Cam 360',
        N'sentinel-cam-360',
        N'Indoor 360 security camera, 2K, night vision.',
        'E0000002-0002-0002-0002-000000000002',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'skyhawk-drone-4k')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000003-0003-0003-0003-000000000003',
        N'Skyhawk Drone 4K',
        N'skyhawk-drone-4k',
        N'Foldable 4K drone, 30min flight, obstacle avoidance.',
        'E0000003-0003-0003-0003-000000000003',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'optic-mirrorless-m50')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000004-0004-0004-0004-000000000004',
        N'Optic Mirrorless M50',
        N'optic-mirrorless-m50',
        N'24MP mirrorless camera body with kit lens option.',
        'E0000002-0002-0002-0002-000000000002',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'vault-ssd-1tb')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000005-0005-0005-0005-000000000005',
        N'Vault SSD 1TB',
        N'vault-ssd-1tb',
        N'NVMe Gen4 SSD 1TB, 7300MB/s read.',
        'E0000004-0004-0004-0004-000000000004',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'vault-hdd-4tb')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000006-0006-0006-0006-000000000006',
        N'Vault HDD 4TB',
        N'vault-hdd-4tb',
        N'4TB external HDD for backups.',
        'E0000004-0004-0004-0004-000000000004',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'volt-powerbank-20000')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000007-0007-0007-0007-000000000007',
        N'Volt Powerbank 20000',
        N'volt-powerbank-20000',
        N'20000mAh powerbank, 65W PD, display.',
        '44444444-4444-4444-4444-444444444444',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'thunder-cable-240w-2m')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000008-0008-0008-0008-000000000008',
        N'Thunder Cable 240W 2m',
        N'thunder-cable-240w-2m',
        N'Braided USB-C 240W 2m cable.',
        'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'control-gamepad-pro')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000009-0009-0009-0009-000000000009',
        N'Control Gamepad Pro',
        N'control-gamepad-pro',
        N'Wireless pro gamepad, hall-effect sticks.',
        '88888888-8888-8888-8888-888888888888',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'desk-dock-pro-13in1')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000010-0010-0010-0010-000000000010',
        N'Desk Dock Pro 13in1',
        N'desk-dock-pro-13in1',
        N'13-in-1 docking station, dual 4K, 90W PD.',
        '99999999-9999-9999-9999-999999999999',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'folio-keyboard-tab-11')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000011-0011-0011-0011-000000000011',
        N'Folio Keyboard Tab 11',
        N'folio-keyboard-tab-11',
        N'Magnetic folio keyboard for Pulse Tab 11.',
        '55555555-5555-5555-5555-555555555555',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'titan-charger-140w')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000012-0012-0012-0012-000000000012',
        N'Titan Charger 140W',
        N'titan-charger-140w',
        N'GaN 140W 3-port charger for laptops.',
        '44444444-4444-4444-4444-444444444444',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'halo-desk-lamp-pro')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000013-0013-0013-0013-000000000013',
        N'Halo Desk Lamp Pro',
        N'halo-desk-lamp-pro',
        N'Smart desk lamp, adaptive color temperature.',
        '99999999-9999-9999-9999-999999999999',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'boombox-party-xl')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000014-0014-0014-0014-000000000014',
        N'Boombox Party XL',
        N'boombox-party-xl',
        N'80W party speaker, lights, mic input.',
        '77777777-7777-7777-7777-777777777777',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Slug = N'nova-fit-band-2')
BEGIN
    INSERT INTO dbo.Products (Id, Name, Slug, Description, CategoryId, Status, Visibility, IsDeleted, CreatedAtUtc, DeletedAtUtc, Seo)
    VALUES (
        'E1000015-0015-0015-0015-000000000015',
        N'Nova Fit Band 2',
        N'nova-fit-band-2',
        N'Budget fitness band, SpO2, 21 days battery.',
        '66666666-6666-6666-6666-666666666666',
        1, 0, 0, SYSUTCDATETIME(), NULL, NULL
    );
END
GO

-- ============================================================
-- 8) EXTRA Variants (batch 2)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'ECHO-MINI-CHALK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000001-0001-0001-0001-000000000001',
        'E1000001-0001-0001-0001-000000000001',
        N'ECHO-MINI-CHALK',
        N'Echo Mini / Chalk',
        49.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'ECHO-MINI-CHAR')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000001-0001-0001-0001-000000000002',
        'E1000001-0001-0001-0001-000000000001',
        N'ECHO-MINI-CHAR',
        N'Echo Mini / Charcoal',
        49.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'SENTINEL-360-WHT')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000002-0002-0002-0002-000000000002',
        'E1000002-0002-0002-0002-000000000002',
        N'SENTINEL-360-WHT',
        N'Sentinel 360 / White',
        89.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'SKYHAWK-4K-FLYMORE')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000003-0003-0003-0003-000000000003',
        'E1000003-0003-0003-0003-000000000003',
        N'SKYHAWK-4K-FLYMORE',
        N'Skyhawk 4K / Fly More combo',
        799.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'SKYHAWK-4K-STD')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000003-0003-0003-0003-000000000004',
        'E1000003-0003-0003-0003-000000000003',
        N'SKYHAWK-4K-STD',
        N'Skyhawk 4K / Standard',
        599.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'OPTIC-M50-BODY')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000004-0004-0004-0004-000000000004',
        'E1000004-0004-0004-0004-000000000004',
        N'OPTIC-M50-BODY',
        N'Optic M50 / Body only',
        649.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'OPTIC-M50-KIT1545')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000004-0004-0004-0004-000000000005',
        'E1000004-0004-0004-0004-000000000004',
        N'OPTIC-M50-KIT1545',
        N'Optic M50 / 15-45mm kit',
        799.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'VAULT-SSD-1TB')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000005-0005-0005-0005-000000000005',
        'E1000005-0005-0005-0005-000000000005',
        N'VAULT-SSD-1TB',
        N'Vault SSD / 1TB',
        89.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'VAULT-SSD-2TB')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000005-0005-0005-0005-000000000006',
        'E1000005-0005-0005-0005-000000000005',
        N'VAULT-SSD-2TB',
        N'Vault SSD / 2TB',
        149.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'VAULT-HDD-4TB')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000006-0006-0006-0006-000000000006',
        'E1000006-0006-0006-0006-000000000006',
        N'VAULT-HDD-4TB',
        N'Vault HDD / 4TB',
        109.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'VOLT-PB20K-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000007-0007-0007-0007-000000000007',
        'E1000007-0007-0007-0007-000000000007',
        N'VOLT-PB20K-BLK',
        N'Volt Powerbank 20K / Black',
        55.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'THUNDER-C240W-2M')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000008-0008-0008-0008-000000000008',
        'E1000008-0008-0008-0008-000000000008',
        N'THUNDER-C240W-2M',
        N'Thunder Cable / 2m / Black',
        19.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'THUNDER-C240W-1M')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000008-0008-0008-0008-000000000009',
        'E1000008-0008-0008-0008-000000000008',
        N'THUNDER-C240W-1M',
        N'Thunder Cable / 1m / White',
        15.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'CONTROL-PAD-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000009-0009-0009-0009-000000000009',
        'E1000009-0009-0009-0009-000000000009',
        N'CONTROL-PAD-BLK',
        N'Control Pad Pro / Black',
        79.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'CONTROL-PAD-WHT')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000009-0009-0009-0009-000000000010',
        'E1000009-0009-0009-0009-000000000009',
        N'CONTROL-PAD-WHT',
        N'Control Pad Pro / White',
        79.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'DESKDOCK-13IN1')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000010-0010-0010-0010-000000000010',
        'E1000010-0010-0010-0010-000000000010',
        N'DESKDOCK-13IN1',
        N'Desk Dock Pro / 13in1',
        199.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'FOLIO-T11-GRY')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000011-0011-0011-0011-000000000011',
        'E1000011-0011-0011-0011-000000000011',
        N'FOLIO-T11-GRY',
        N'Folio Keyboard / Grey',
        89.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'TITAN-140W-GAN')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000012-0012-0012-0012-000000000012',
        'E1000012-0012-0012-0012-000000000012',
        N'TITAN-140W-GAN',
        N'Titan 140W / GaN / Black',
        99.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'HALO-LAMP-WHT')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000013-0013-0013-0013-000000000013',
        'E1000013-0013-0013-0013-000000000013',
        N'HALO-LAMP-WHT',
        N'Halo Lamp Pro / White',
        69.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'BOOMBOX-XL-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000014-0014-0014-0014-000000000014',
        'E1000014-0014-0014-0014-000000000014',
        N'BOOMBOX-XL-BLK',
        N'Boombox XL / Black',
        249.000000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'NOVAFIT-B2-BLK')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000015-0015-0015-0015-000000000015',
        'E1000015-0015-0015-0015-000000000015',
        N'NOVAFIT-B2-BLK',
        N'Nova Fit Band 2 / Black',
        39.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Variants WHERE Sku = N'NOVAFIT-B2-BLU')
BEGIN
    INSERT INTO dbo.Variants (Id, ProductId, Sku, Name, UnitPriceAmount, UnitPriceCurrency, Status, IsDeleted, CreatedAtUtc, DeletedAtUtc)
    VALUES (
        'E2000015-0015-0015-0015-000000000016',
        'E1000015-0015-0015-0015-000000000015',
        N'NOVAFIT-B2-BLU',
        N'Nova Fit Band 2 / Blue',
        39.990000, N'USD',
        0, 0, SYSUTCDATETIME(), NULL
    );
END
GO

-- ============================================================
-- 9) EXTRA Images + Specifications (batch 2)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/skyhawk/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'E3000003-0003-0003-0003-000000000003',
        'E1000003-0003-0003-0003-000000000003',
        N'https://picsum.photos/seed/skyhawk/800/600',
        N'Skyhawk drone in flight',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/optic-m50/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'E3000004-0004-0004-0004-000000000004',
        'E1000004-0004-0004-0004-000000000004',
        N'https://picsum.photos/seed/optic-m50/800/600',
        N'Optic M50 camera',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/control-pad/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'E3000009-0009-0009-0009-000000000009',
        'E1000009-0009-0009-0009-000000000009',
        N'https://picsum.photos/seed/control-pad/800/600',
        N'Control gamepad pro',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/deskdock/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'E3000010-0010-0010-0010-000000000010',
        'E1000010-0010-0010-0010-000000000010',
        N'https://picsum.photos/seed/deskdock/800/600',
        N'Desk dock pro',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/boombox-xl/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'E3000014-0014-0014-0014-000000000014',
        'E1000014-0014-0014-0014-000000000014',
        N'https://picsum.photos/seed/boombox-xl/800/600',
        N'Boombox party speaker',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductImages WHERE ImageUrl = N'https://picsum.photos/seed/novafit-b2/800/600')
BEGIN
    INSERT INTO dbo.ProductImages (Id, ProductId, ImageUrl, AltText, DisplayOrder, IsPrimary, Metadata, CreatedAtUtc)
    VALUES (
        'E3000015-0015-0015-0015-000000000015',
        'E1000015-0015-0015-0015-000000000015',
        N'https://picsum.photos/seed/novafit-b2/800/600',
        N'Nova fit band 2',
        0, 1, NULL, SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'E1000003-0003-0003-0003-000000000003' AND Name = N'FlightTime')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('E4000003-0003-0003-0003-000000000003', 'E1000003-0003-0003-0003-000000000003', N'FlightTime', N'30 min', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'E1000004-0004-0004-0004-000000000004' AND Name = N'Sensor')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('E4000004-0004-0004-0004-000000000004', 'E1000004-0004-0004-0004-000000000004', N'Sensor', N'24MP APS-C', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'E1000005-0005-0005-0005-000000000005' AND Name = N'ReadSpeed')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('E4000005-0005-0005-0005-000000000005', 'E1000005-0005-0005-0005-000000000005', N'ReadSpeed', N'7300MB/s NVMe Gen4', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'E1000010-0010-0010-0010-000000000010' AND Name = N'Ports')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('E4000010-0010-0010-0010-000000000010', 'E1000010-0010-0010-0010-000000000010', N'Ports', N'13-in-1 dual 4K 90W PD', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'E1000014-0014-0014-0014-000000000014' AND Name = N'Power')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('E4000014-0014-0014-0014-000000000014', 'E1000014-0014-0014-0014-000000000014', N'Power', N'80W with mic input', SYSUTCDATETIME());
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductSpecifications WHERE ProductId = 'E1000015-0015-0015-0015-000000000015' AND Name = N'Battery')
BEGIN
    INSERT INTO dbo.ProductSpecifications (Id, ProductId, Name, Value, CreatedAtUtc)
    VALUES ('E4000015-0015-0015-0015-000000000015', 'E1000015-0015-0015-0015-000000000015', N'Battery', N'21 days typical', SYSUTCDATETIME());
END
GO

COMMIT TRANSACTION;
GO

-- Verification
SELECT 'Categories' AS [Table], COUNT(*) AS [Count] FROM dbo.Categories
UNION ALL SELECT 'Products', COUNT(*) FROM dbo.Products
UNION ALL SELECT 'Variants', COUNT(*) FROM dbo.Variants
UNION ALL SELECT 'ProductImages', COUNT(*) FROM dbo.ProductImages
UNION ALL SELECT 'ProductSpecifications', COUNT(*) FROM dbo.ProductSpecifications;
GO
