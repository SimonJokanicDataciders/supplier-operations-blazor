# C# Learnings

This project is a small Blazor + EF Core inventory app, but it touches the main C# web concepts that matter for backend and full-stack work.

## Minimal APIs

Minimal APIs expose data and app actions through endpoints without requiring full MVC controllers. In this app, they are used for dashboard, products, suppliers, supplier import, supplier operation summaries, stock adjustment, purchase orders, and AI usage records.

What this teaches:

- map HTTP routes to C# handlers
- return DTOs instead of raw database entities
- keep API responses predictable for a UI or another app

## DTOs

DTOs are small request and response shapes made for a specific API or UI use case. They stop the app from leaking full EF Core models everywhere.

What this teaches:

- separate database models from API contracts
- keep import payloads stable
- return only the fields a screen actually needs

## EF Core Relationships

The app connects suppliers, products, categories, stock movements, and purchase orders through EF Core relationships.

What this teaches:

- one supplier can have many products
- one category can have many products
- one product can have many stock movements
- one supplier can have many purchase orders
- one purchase order can have many purchase order lines
- one product can appear on many purchase order lines
- `Include` loads related data when the UI needs it

## Blazor Forms

Blazor forms handle product creation, supplier creation, supplier import, stock in, stock out, stock adjustment, and purchase order receiving.

What this teaches:

- bind Razor inputs to C# form models
- validate user input with data annotations
- handle submit events in C#
- show feedback after an operation succeeds or fails
- accept a final counted quantity and let C# calculate the movement delta

## Query Parameters

The app uses query parameters to preselect a supplier when creating a product from a supplier detail page.

What this teaches:

- pass small pieces of UI state through the URL
- create cleaner flows between pages
- return the user to the correct page after saving

## SQLite Schema Changes

The app uses SQLite locally and applies additive schema changes at startup.

What this teaches:

- persist data locally
- evolve tables without deleting the database
- keep learning projects easy to run without a production migration setup

## Service Layer Logic

The `InventoryService` holds the main business logic for products, suppliers, imports, stock movements, and purchase orders.

What this teaches:

- keep database logic out of Razor pages
- reuse logic between UI screens and API endpoints
- enforce business rules in one place, like preventing negative stock
- reuse the same stock movement rules for quick actions, adjustment batches, and APIs
- keep purchase order status rules out of the Razor pages

## Purchase Order Flow

Purchase orders add a small operations workflow on top of inventory tracking.

What this teaches:

- create a draft record first, then move it through clear statuses
- block invalid transitions, like receiving a draft or cancelled order
- receive partial quantities without losing the remaining open quantity
- update several tables together when stock arrives
- create a `StockIn` audit movement at the same time product stock increases

## Calculated State Changes

Stock adjustment is based on the final count, not on the user manually choosing `+` or `-`.

What this teaches:

- compare current stock with the counted quantity
- calculate the difference before saving a movement
- reject invalid counts before changing inventory
- keep the saved movement as the audit trail for the state change

## API Aggregation

Dashboard and stock movement summary APIs turn many rows into compact totals for the UI.

What this teaches:

- group stock movement data by type or purpose
- return summary DTOs instead of raw movement rows
- keep aggregation logic reusable between dashboard screens and API endpoints

## AI Usage Tracking

The AI usage page is a small ledger for LLM cost awareness. It uses a price catalog for known provider/model rates, but each usage record keeps the price snapshot that was used when the run was logged.

What this teaches:

- keep the default form focused on the fields most people actually need
- move provider-routing and manual-price fields behind an advanced toggle
- store operational telemetry as normal database records
- separate token volume, estimated cost, and actual billed cost
- calculate cost from input, cached input, output, and search-call prices
- keep provider pricing editable because public model rates change
- support custom/OpenRouter-style routes without changing the usage table again
- wire a paste-based CSV/JSON import panel to the backend import workflow
