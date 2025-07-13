FROM nginx:alpine
RUN echo 'Test Container Deployed Successfully!' > /usr/share/nginx/html/index.html && \
    echo 'Test Container Deployed Successfully!' > /usr/share/nginx/html/health
COPY nginx.conf /etc/nginx/nginx.conf
EXPOSE 80 8080