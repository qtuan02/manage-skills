FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 9920

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["HealthExam.API/HealthExam.API.csproj", "HealthExam.API/"]
COPY ["HealthExam.Domain/HealthExam.Domain.csproj", "HealthExam.Domain/"]
COPY ["HealthExam.Application/HealthExam.Application.csproj", "HealthExam.Application/"]
COPY ["HealthExam.Infrastructure/HealthExam.Infrastructure.csproj", "HealthExam.Infrastructure/"]
RUN dotnet restore "HealthExam.API/HealthExam.API.csproj"

COPY . .
RUN dotnet publish "HealthExam.API/HealthExam.API.csproj" -c Release -o /app --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app .

RUN apt-get update && \
    apt-get install -y --no-install-recommends libicu-dev tzdata curl && \
    rm -rf /var/lib/apt/lists/*

ENV TZ=Asia/Ho_Chi_Minh
ENV ASPNETCORE_URLS=http://+:9920

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://127.0.0.1:9920/health || exit 1

ENTRYPOINT ["dotnet", "HealthExam.API.dll"]
