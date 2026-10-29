#!/bin/sh
# R3b's floor searches for the five AWS packages (plans/CLIENT_FLOORS_PLAN.md 5.2): each AWSSDK reference binary-searched
# over its major with floor_probe2.py, one after another (the searches share the worktree's builds). No suite needs a
# server: each runs against the SDK's own handler pipeline.
#   sh r3b-search-aws.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
search() {
  name=$1; project=$2; package=$3; shift 3
  python -I "$H/floor_probe2.py" --worktree "$W" --project "src/$project/$project.csproj" --package "$package" \
    "$@" --out "$H/r3b-$name.txt" --logs "$LOGS/$name" --search
}
search s3 Kronikol.Extensions.S3 AWSSDK.S3 --tests tests/Kronikol.Tests.S3
search dynamodb Kronikol.Extensions.DynamoDB AWSSDK.DynamoDBv2 --tests tests/Kronikol.Tests.DynamoDB
search sns Kronikol.Extensions.SNS AWSSDK.SimpleNotificationService --tests tests/Kronikol.Tests.SNS
search sqs Kronikol.Extensions.SQS AWSSDK.SQS --tests tests/Kronikol.Tests.SQS
search eventbridge Kronikol.Extensions.EventBridge AWSSDK.EventBridge --tests tests/Kronikol.Tests.EventBridge
echo ALLDONE > "$H/r3b-search-aws.done"
