import { expect, type APIRequestContext } from "@playwright/test";
import { Api, readWorld, type AipRole } from "./seed";

// Puts the E2E office back where a journey starts: editable by the office, no projects in its
// programs. Whatever an earlier run left (a pass ends Consolidated; a failure can stop anywhere),
// it walks the office back with the same calls the reviewer and the department head have:
//   Consolidated    → reviewer reopens   → ReturnedByPpdo
//   SubmittedToPpdo → reviewer returns   → ReturnedByPpdo
//   DepartmentReview / submitted divisions → head returns each division
// Only ever touches the E2E office.

type Json = Record<string, unknown>;

async function as(request: APIRequestContext, role: AipRole): Promise<Api> {
  const a = readWorld().accounts[role];
  const api = await Api.login(request, a.username, a.password);
  expect(api, `login as ${a.username} failed`).not.toBeNull();
  return api!;
}

export async function officeStatus(head: Api, aipId: number): Promise<Json> {
  return head.call<Json>("get", `/budget-planning/aip/${aipId}/readiness`);
}

export async function resetAipOffice(request: APIRequestContext): Promise<void> {
  const w = readWorld();
  const head = await as(request, "head");
  const reviewer = await as(request, "reviewer");
  const office = `/budget-planning/aip/${w.aipId}/offices/${w.officeId}`;

  for (let i = 0; i < 4; i++) {
    const status = (await officeStatus(head, w.aipId)).workflowStatus as string;
    if (status === "Consolidated") await reviewer.call("post", `${office}/reopen`, {});
    else if (status === "SubmittedToPpdo") await reviewer.call("post", `${office}/return`, {});
    else break;
  }
  // A division that was submitted (to the department head) goes back to its encoder. A division
  // that wasn't refuses; that refusal is the "already editable" answer, so it is ignored.
  for (const divisionId of [w.divisionIds.A, w.divisionIds.B]) {
    await head.call("post", `/budget-planning/aip/${w.aipId}/divisions/${divisionId}/return`, {}).catch(() => {});
  }
  const status = (await officeStatus(head, w.aipId)).workflowStatus as string;
  expect(["Draft", "ReturnedByPpdo"], `E2E office stuck in ${status}`).toContain(status);

  // Empty tree: delete every project under the office's programs (activities go with them).
  const detail = await head.call<Json>("get", `/budget-planning/aip/${w.aipId}`);
  const rows = (detail.offices as Json[]).filter((o) => o.officeId === w.officeId);
  for (const row of rows)
    for (const program of row.programs as Json[])
      for (const project of program.projects as Json[])
        await head.call("delete", `/budget-planning/aip/projects/${project.id}`);
}
