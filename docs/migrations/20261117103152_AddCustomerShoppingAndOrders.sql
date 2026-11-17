BEGIN TRANSACTION;
CREATE TABLE [Orders] (
    [Id] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [Number] nvarchar(36) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [IdempotencyKey] uniqueidentifier NOT NULL,
    [CartVersion] uniqueidentifier NOT NULL,
    [Total] decimal(18,2) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [UpdatedByUserId] uniqueidentifier NULL,
    [DeactivatedByUserId] uniqueidentifier NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [DeactivatedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Orders_Status] CHECK ([Status] = 'Pending'),
    CONSTRAINT [CK_Orders_Total] CHECK ([Total] >= 0),
    CONSTRAINT [FK_Orders_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ShoppingCarts] (
    [Id] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [Version] uniqueidentifier NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedByUserId] uniqueidentifier NULL,
    [UpdatedByUserId] uniqueidentifier NULL,
    [DeactivatedByUserId] uniqueidentifier NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [DeactivatedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_ShoppingCarts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ShoppingCarts_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [OrderItems] (
    [Id] uniqueidentifier NOT NULL,
    [OrderId] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [ProductName] nvarchar(200) NOT NULL,
    [ProductBarcode] nvarchar(50) NOT NULL,
    [Quantity] int NOT NULL,
    [UnitPrice] decimal(18,2) NOT NULL,
    [Subtotal] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_OrderItems_Prices] CHECK ([UnitPrice] >= 0 AND [Subtotal] >= 0),
    CONSTRAINT [CK_OrderItems_Quantity] CHECK ([Quantity] >= 1 AND [Quantity] <= 10000),
    CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_OrderItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CartItems] (
    [Id] uniqueidentifier NOT NULL,
    [CartId] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [Quantity] int NOT NULL,
    CONSTRAINT [PK_CartItems] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_CartItems_Quantity] CHECK ([Quantity] >= 1 AND [Quantity] <= 10000),
    CONSTRAINT [FK_CartItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CartItems_ShoppingCarts_CartId] FOREIGN KEY ([CartId]) REFERENCES [ShoppingCarts] ([Id]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_CartItems_CartId_ProductId] ON [CartItems] ([CartId], [ProductId]);

CREATE INDEX [IX_CartItems_ProductId] ON [CartItems] ([ProductId]);

CREATE UNIQUE INDEX [IX_OrderItems_OrderId_ProductId] ON [OrderItems] ([OrderId], [ProductId]);

CREATE INDEX [IX_OrderItems_ProductId] ON [OrderItems] ([ProductId]);

CREATE UNIQUE INDEX [IX_Orders_CustomerId_CartVersion] ON [Orders] ([CustomerId], [CartVersion]);

CREATE INDEX [IX_Orders_CustomerId_CreatedAtUtc] ON [Orders] ([CustomerId], [CreatedAtUtc]);

CREATE UNIQUE INDEX [IX_Orders_CustomerId_IdempotencyKey] ON [Orders] ([CustomerId], [IdempotencyKey]);

CREATE UNIQUE INDEX [IX_Orders_Number] ON [Orders] ([Number]);

CREATE UNIQUE INDEX [IX_ShoppingCarts_CustomerId] ON [ShoppingCarts] ([CustomerId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117103152_AddCustomerShoppingAndOrders', N'10.0.15');

COMMIT;
GO
