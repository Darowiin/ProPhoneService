# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ProPhoneService.Server.slnx ./
COPY ProPhoneService.Domain.Shared/ ./ProPhoneService.Domain.Shared/
COPY ProPhoneService.Domain/ ./ProPhoneService.Domain/
COPY ProPhoneService.Infrastructure.EfCore/ ./ProPhoneService.Infrastructure.EfCore/
COPY ProPhoneService.Application.Contracts/ ./ProPhoneService.Application.Contracts/
COPY ProPhoneService.Application/ ./ProPhoneService.Application/
COPY ProPhoneService.Api/ ./ProPhoneService.Api/

RUN dotnet restore ProPhoneService.Api/ProPhoneService.Api.csproj
RUN dotnet publish ProPhoneService.Api/ProPhoneService.Api.csproj -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "ProPhoneService.Api.dll"]
