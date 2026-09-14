import { createHash } from "node:crypto";

export const md5 = (str: string) => {
  return createHash("md5").update(str).digest("hex");
};
export const getTestResultHistoryId = (result: TestResult) => {
  if (result.historyId) {
    return result.historyId;
  }

  const tcId = result.testCaseId ?? (result.fullName ? md5(result.fullName) : null);

  if (!tcId) {
    return "";
  }

  const paramsString = result.parameters
    .filter((p) => !p?.excluded)
    .sort((a, b) => a.name?.localeCompare(b?.name) || a.value?.localeCompare(b?.value))
    .map((p) => `${p.name ?? "null"}:${p.value ?? "null"}`)
    .join(",");
  const paramsHash = md5(paramsString);

  return `${tcId}:${paramsHash}`;
};