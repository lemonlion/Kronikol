import { getTestResultHistoryId } from "./historyid.ts";
const t = (over: any) => ({ historyId: undefined, testCaseId: undefined,
  fullName: "Checkout > Pay with an expired card", parameters: [], ...over });
const show = (l: string, r: any) => console.log(l.padEnd(52), getTestResultHistoryId(r));
show("plain", t({}));
show("same, parameters REORDERED", t({ parameters: [{name:"b",value:"2"},{name:"a",value:"1"}] }));
show("same, parameters in order", t({ parameters: [{name:"a",value:"1"},{name:"b",value:"2"}] }));
show("one parameter marked excluded", t({ parameters: [{name:"a",value:"1"},{name:"seed",value:"91823",excluded:true}] }));
show("...same but seed NOT excluded", t({ parameters: [{name:"a",value:"1"},{name:"seed",value:"91823"}] }));
show("...seed changes, still excluded", t({ parameters: [{name:"a",value:"1"},{name:"seed",value:"55555",excluded:true}] }));
show("explicit historyId override", t({ historyId: "pinned-across-renames" }));
show("RENAMED, no override", t({ fullName: "Checkout > Pay with a card that expired" }));
