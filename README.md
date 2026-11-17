# Inventory Management ERP

## Project Overview

A portfolio inventory management application with an ASP.NET Core API and a React
client. It covers product and warehouse management, auditable stock operations,
role-based access, and a customer catalog, cart, and order workflow.

This repository contains source code and tests, not a running hosted service.
Real databases, database backups, user accounts, credentials, and customer records
are not included. Database creation and account provisioning are separate tasks.

## Features

- Product barcodes stored as unique strings, preserving leading zeros.
- Products, customers, and warehouses with validation and soft-deactivation rules.
- Stock-in/out operations, non-negative balances, movement history, and low-stock alerts.
- Audit timestamps and actor attribution, with optimistic inventory concurrency control.
- Customer-linked accounts with ownership checks for catalog, cart, and order APIs.
- Server-priced checkout with idempotency keys and order line snapshots.
- A guarded offline CLI for repeatable, synthetic demo inventory.

