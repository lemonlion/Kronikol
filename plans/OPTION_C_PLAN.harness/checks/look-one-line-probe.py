# Writes the probe's JSONL: one sequence source with the look written three ways (none, the mock's multi-line style,
# one line per rule) so the engine's SVGs can be compared byte for byte (S10a: the same SVG, 4,730 bytes, against
# 4,578 with no look). usage: python look-one-line-probe.py > in.jsonl, then
#   node src/Kronikol/PlantUml/plantuml-render.js <engine>/viz-global.js <engine>/plantuml.js --batch < in.jsonl
import json
BODY = '''actor "WebApp" as webApp
participant "OrderService" as orderService
database "Cosmos DB" as cosmosDB #0E7C86
webApp -> orderService: GET /api/orders
note right
{ "id": 1 }
end note
orderService -> cosmosDB: Query
orderService --> webApp: 200 OK
'''
HEAD = '@startuml\n!pragma teoz true\n'
SKIN = 'skinparam shadowing false\nskinparam SequenceMessagePadding 4\nskinparam sequenceMessageAlignment center\n'
MULTI = ('<style>\nsequenceDiagram {\n'
         '  participant {\n    RoundCorner 12\n    Padding 8 14\n    Margin 0 10\n    LineThickness 1\n  }\n'
         '  actor, database, queue, collections, boundary, control, entity {\n    Margin 0 10\n    LineThickness 1\n  }\n'
         '  lifeLine {\n    LineStyle 0\n    LineThickness 1\n  }\n'
         '  note {\n    LineThickness 1\n  }\n'
         '}\n</style>\n')
ONE = ('<style>\n'
       'sequenceDiagram { participant { RoundCorner 12; Padding 8 14; Margin 0 10; LineThickness 1 } }\n'
       'sequenceDiagram { actor, database, queue, collections, boundary, control, entity { Margin 0 10; LineThickness 1 } }\n'
       'sequenceDiagram { lifeLine { LineStyle 0; LineThickness 1 } }\n'
       'sequenceDiagram { note { LineThickness 1 } }\n'
       '</style>\n')
for id_, look in (('none', ''), ('multi', SKIN + MULTI), ('one', SKIN + ONE)):
    print(json.dumps({'id': id_, 'source': HEAD + look + BODY + '@enduml\n'}))
