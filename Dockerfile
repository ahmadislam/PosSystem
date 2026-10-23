FROM node:24-bookworm-slim AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN POS_API_ORIGIN=same-origin NG_BUILD_SASS_EMBEDDED=false npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/PosSystem.Api/PosSystem.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=web /web/dist/web/browser ./wwwroot
ENV ASPNETCORE_URLS=http://+:10000
ENV Web__ServeClient=true
EXPOSE 10000
ENTRYPOINT ["dotnet", "PosSystem.Api.dll"]
