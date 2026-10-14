FROM alpine:3.22.1
RUN apk add --no-cache openssl
COPY docker/local-init/init.sh /usr/local/bin/lms-local-init
COPY docker/keycloak/sapientum-local-realm.template.json /opt/lms/sapientum-local-realm.template.json
ENTRYPOINT ["/usr/local/bin/lms-local-init"]
