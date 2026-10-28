"""plans/SERVICE_CARD_PLAN.md 5.3.1: the NDJSON capture both ingests read, 4.32.0's and the current one.

Two tests: an order whose request body is 9,000 characters (so a 4.x writer stores it compressed and the file is
formatVersion 2), with the service's call to a dependency inside it on the same trace; and a menu read. Ids and times are
fixed so the file is the same on every run of this script. Usage: python -I gen_ndjson.py <out.ndjson>
"""
import json
import sys

notes = ("Two pancakes, golden on both sides, with butter melting on the top one and maple syrup on the side. " * 100)[:9000]
order = json.dumps({"dish": "pancakes", "count": 2, "notes": notes}, separators=(",", ":"))
trace = "8f4c6a52-1b7d-4e39-9a0e-3c5d7e9f1a2b"
lines = [
    {"type": "Request", "method": "POST", "uri": "http://localhost/orders", "serviceName": "Breakfast Provider", "callerName": "Caller",
     "content": order, "traceId": trace, "requestResponseId": "0b9d8c7e-6f5a-4b3c-8d2e-1f0a9b8c7d6e", "timestamp": "2026-10-28T09:00:00.000+00:00",
     "testId": "t-order", "testName": "Order pancakes", "phase": "Action"},
    {"type": "Request", "method": "GET", "uri": "http://localhost:5001/milk/1", "serviceName": "Cow Service", "callerName": "Breakfast Provider",
     "traceId": trace, "requestResponseId": "1c0e9d8f-7a6b-4c5d-9e3f-2a1b0c9d8e7f", "timestamp": "2026-10-28T09:00:00.010+00:00",
     "testId": "t-order", "testName": "Order pancakes"},
    {"type": "Response", "method": "GET", "uri": "http://localhost:5001/milk/1", "serviceName": "Cow Service", "callerName": "Breakfast Provider",
     "statusCode": "200", "traceId": trace, "requestResponseId": "1c0e9d8f-7a6b-4c5d-9e3f-2a1b0c9d8e7f", "timestamp": "2026-10-28T09:00:00.020+00:00",
     "testId": "t-order", "testName": "Order pancakes"},
    {"type": "Response", "method": "POST", "uri": "http://localhost/orders", "serviceName": "Breakfast Provider", "callerName": "Caller",
     "statusCode": "201", "content": "{\"id\":123}", "traceId": trace, "requestResponseId": "0b9d8c7e-6f5a-4b3c-8d2e-1f0a9b8c7d6e",
     "timestamp": "2026-10-28T09:00:00.030+00:00", "testId": "t-order", "testName": "Order pancakes", "phase": "Action"},
    {"type": "Request", "method": "GET", "uri": "http://localhost/menu", "serviceName": "Breakfast Provider", "callerName": "Caller",
     "traceId": "9a5d7b63-2c8e-4f4a-8b1f-4d6e8f0a2b3c", "requestResponseId": "2d1f0e9a-8b7c-4d6e-8f4a-3b2c1d0e9f8a",
     "timestamp": "2026-10-28T09:00:01.000+00:00", "testId": "t-menu", "testName": "Read the menu", "phase": "Action"},
    {"type": "Response", "method": "GET", "uri": "http://localhost/menu", "serviceName": "Breakfast Provider", "callerName": "Caller",
     "statusCode": "200", "traceId": "9a5d7b63-2c8e-4f4a-8b1f-4d6e8f0a2b3c", "requestResponseId": "2d1f0e9a-8b7c-4d6e-8f4a-3b2c1d0e9f8a",
     "timestamp": "2026-10-28T09:00:01.010+00:00", "testId": "t-menu", "testName": "Read the menu", "phase": "Action"},
]
with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as f:
    for line in lines:
        f.write(json.dumps(line, separators=(",", ":"), ensure_ascii=False) + "\n")
print(f"{len(lines)} lines, request body {len(order)} characters")
