# C# Learnings

This project is a small Blazor + EF Core inventory app, but it touches the main C# web concepts that matter for backend and full-stack work.

## Minimal APIs

Minimal APIs expose data and app actions through endpoints without requiring full MVC controllers. In this app, they are used for dashboard, products, suppliers, supplier import, and supplier operation summaries.

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

The app connects suppliers, products, categories, and stock movements through EF Core relationships.

What this teaches:

- one supplier can have many products
- one category can have many products
- one product can have many stock movements
- `Include` loads related data when the UI needs it

## Blazor Forms

Blazor forms handle product creation, supplier creation, supplier import, stock in, stock out, and stock adjustment.

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

The `InventoryService` holds the main business logic for products, suppliers, imports, and stock movements.

What this teaches:

- keep database logic out of Razor pages
- reuse logic between UI screens and API endpoints
- enforce business rules in one place, like preventing negative stock
- reuse the same stock movement rules for quick actions, adjustment batches, and APIs

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
