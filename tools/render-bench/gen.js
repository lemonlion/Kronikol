function gen(n) {
  const L = ['@startuml', 'skinparam responseMessageBelowArrow true', 'participant "Test" as T', 'participant "Example.Api" as A', 'database "OrdersDb" as D', 'participant "External" as X'];
  for (let i = 0; i < n; i++) {
    L.push('T -> A: GET /orders/' + i + '?include=lines');
    L.push('A -> D: SELECT o.id, o.total FROM orders o WHERE o.id = ' + i);
    L.push('D --> A: 1 row (id=' + i + ')');
    if (i % 3 === 0) { L.push('note right of A'); L.push('{ "id": ' + i + ', "customer": "cust-' + i + '", "total": ' + (i * 3.5).toFixed(2) + ' }'); L.push('end note'); }
    if (i % 5 === 0) { L.push('A -> X: POST /audit/' + i); L.push('X --> A: 202 Accepted'); }
    L.push('A --> T: 200 OK (' + (i * 7 % 40) + ' ms)');
  }
  L.push('@enduml');
  return L;
}
module.exports = { gen };
