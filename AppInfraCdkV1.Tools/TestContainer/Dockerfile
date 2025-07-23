FROM --platform=linux/amd64 nginx:alpine
# Alternative: FROM nginx:1.24-alpine-slim (this is typically AMD64)

# Install wget for health checks
RUN apk add --no-cache wget

RUN echo 'Test Container Deployed Successfully!' > /usr/share/nginx/html/index.html && \
    echo 'Test Container Deployed Successfully!' > /usr/share/nginx/html/health
COPY nginx.conf /etc/nginx/nginx.conf
EXPOSE 80 8080