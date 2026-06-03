FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

RUN mkdir -p /app/uploads && chmod -R 777 /app/uploads

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["FileSite/FileSite.csproj", "FileSite/"]
RUN dotnet restore "FileSite/FileSite.csproj"
COPY . .
WORKDIR "/src/FileSite"
RUN dotnet build "FileSite.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FileSite.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FileSite.dll"]
