FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /app

COPY ./Services/Sample ./Services/Sample
COPY ./Shared ./Shared

RUN dotnet restore ./Services/Sample/Sample.Service/Sample.Service.csproj
RUN dotnet publish ./Services/Sample/Sample.Service/Sample.Service.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0

WORKDIR /app

COPY --from=build /out .

ENTRYPOINT ["dotnet", "Sample.Service.dll"]
