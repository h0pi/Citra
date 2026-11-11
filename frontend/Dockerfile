# The Citra admin app. Built from the frontend folder:  docker build frontend
# Stage 1 builds the Angular app; stage 2 serves it with nginx, which also forwards the API's addresses to the
# api container, so the browser sees one site (no CORS, same-origin cookies and SignalR).

FROM node:24-slim AS build
WORKDIR /app

# The npm named in package.json ("packageManager"): newer ones read the lock file differently.
COPY package.json package-lock.json ./
RUN npm install -g "$(node -p "require('./package.json').packageManager")" && npm ci

COPY . .
RUN npm run build

FROM nginx:1.29-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/citra-admin/browser /usr/share/nginx/html
EXPOSE 8080
