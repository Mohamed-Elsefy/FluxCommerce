# Local Development

## Start Infrastructure

docker compose up -d

or

docker compose up -d smtp4dev

---

## Check Containers

docker compose ps

docker compose ps smtp4dev

---

## Run API

dotnet run --project src/FluxCommerce.Api

or

dotnet run --project src/FluxCommerce.Api --launch-profile https

Swagger

http://localhost:5260/swagger

https://localhost:7119/swagger

smtp4dev

http://localhost:5000

---

## Useful Docker Commands

docker compose logs smtp4dev

docker compose restart smtp4dev

docker compose stop smtp4dev

docker compose down
