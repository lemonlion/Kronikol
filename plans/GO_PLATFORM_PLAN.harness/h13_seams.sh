#!/usr/bin/env bash
# H13. The seams the libraries a Go component test meets expose, read with `go doc` from their source
# at the version the module proxy calls latest today. Nothing here runs a database or a broker.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"; cd "$work"
export GOTOOLCHAIN="${GOTOOLCHAIN:-go1.27.1}" GOFLAGS=-mod=mod
go mod init h13 >/dev/null 2>&1
mods="github.com/jackc/pgx/v5 github.com/redis/go-redis/v9 go.mongodb.org/mongo-driver/v2 google.golang.org/grpc
github.com/IBM/sarama github.com/twmb/franz-go github.com/segmentio/kafka-go github.com/aws/aws-sdk-go-v2
github.com/cucumber/godog github.com/onsi/ginkgo/v2 github.com/stretchr/testify go.opentelemetry.io/otel/sdk
gorm.io/driver/postgres github.com/testcontainers/testcontainers-go"
for m in $mods; do
  v=$(curl -s -m 20 "https://proxy.golang.org/$m/@latest" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["Version"], d["Time"][:10])' 2>/dev/null)
  echo "module $m  latest $v"
done
get() { go get "$1" >/dev/null 2>&1 || echo "  (go get $1 failed)"; }
show() { echo "---- go doc $1"; go doc "$1" 2>&1 | grep -v '^$' | sed -n "1,${2:-14}p"; }
get github.com/jackc/pgx/v5@latest; get github.com/redis/go-redis/v9@latest; get go.mongodb.org/mongo-driver/v2@latest
get google.golang.org/grpc@latest; get github.com/IBM/sarama@latest; get github.com/twmb/franz-go@latest
get github.com/segmentio/kafka-go@latest; get github.com/aws/aws-sdk-go-v2@latest; get github.com/cucumber/godog@latest
get github.com/onsi/ginkgo/v2@latest; get github.com/stretchr/testify@latest; get go.opentelemetry.io/otel/sdk@latest
get gorm.io/driver/postgres@latest
show github.com/jackc/pgx/v5.QueryTracer 12
show github.com/jackc/pgx/v5.TraceQueryStartData 8
show github.com/jackc/pgx/v5.ConnConfig 20
show github.com/redis/go-redis/v9.Hook 12
show go.mongodb.org/mongo-driver/v2/event.CommandMonitor 10
show go.mongodb.org/mongo-driver/v2/event.CommandStartedEvent 12
show go.mongodb.org/mongo-driver/v2/event.CommandSucceededEvent 10
show google.golang.org/grpc.UnaryClientInterceptor 6
show google.golang.org/grpc.WithChainUnaryInterceptor 6
show google.golang.org/grpc.UnaryServerInterceptor 6
show github.com/IBM/sarama.ProducerInterceptor 8
show github.com/twmb/franz-go/pkg/kgo.HookProduceRecordBuffered 8
show github.com/segmentio/kafka-go.RoundTripper 8
show github.com/aws/aws-sdk-go-v2/aws.HTTPClient 6
show github.com/cucumber/godog.ScenarioContext 30
show github.com/cucumber/godog.TestSuite 20
show github.com/onsi/ginkgo/v2.ReportAfterEach 8
show github.com/stretchr/testify/assert.TestingT 6
show github.com/stretchr/testify/suite.Suite 12
show go.opentelemetry.io/otel/sdk/trace.SpanExporter 10
show gorm.io/driver/postgres.Config 14
echo "---- godog's formatters (none writes Cucumber Messages, which kronikol ingest reads)"
grep -rhn 'formatters.Format("' "$(go env GOMODCACHE)"/github.com/cucumber/godog@*/internal/formatters/*.go | grep -v _test | sed 's/^[0-9]*:\s*//'
