import Handlebars from "handlebars";
import fs from "node:fs";
// only the two helpers flaky-table.hbs uses, registered from the reporter's own source
import { anyFlakyTestsHelper, getEmojiHelper } from "./h-ctrf.ts";
anyFlakyTestsHelper();
getEmojiHelper();

const tmpl = Handlebars.compile(fs.readFileSync(new URL("./flaky-table.hbs", import.meta.url), "utf8"));
const render = (label: string, tests: any[]) => {
  console.log(`\n===== ${label} =====`);
  console.log(tmpl({ ctrf: { tests } }).split("\n").filter(l => l.trim()).join("\n"));
};
render("KRONIKOL: flaky:true, no retries", [
  { name: "Pay with an expired card", status: "passed", flaky: true },
  { name: "Checkout completes", status: "passed" },
]);
render("FREE CHAIN self-detected: retries:2, no flaky flag", [
  { name: "Pay with an expired card", status: "passed", retries: 2 },
]);
render("NEITHER", [{ name: "Checkout completes", status: "passed" }]);
