FROM alpine:3.22.1
RUN apk add --no-cache ca-certificates curl jq
COPY docker/operator-token/token.sh /usr/local/bin/sapientum-operator-token
ENTRYPOINT ["/usr/local/bin/sapientum-operator-token"]
