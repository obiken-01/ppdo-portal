import { describe, expect, it } from "vitest";
import { createInFlightLock } from "./in-flight-lock";

function deferred<T>() {
  let resolve!: (v: T) => void;
  let reject!: (e: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

describe("createInFlightLock", () => {
  it("run_SecondCallWhileFirstPending_IsDropped", async () => {
    const lock = createInFlightLock();
    const gate = deferred<string>();
    let calls = 0;
    const save = () => { calls++; return gate.promise; };

    const first = lock.run(save);
    const second = lock.run(save);

    expect(calls).toBe(1);
    expect(await second).toBeUndefined();

    gate.resolve("saved");
    expect(await first).toBe("saved");
  });

  it("run_AfterFirstSettles_RunsAgain", async () => {
    const lock = createInFlightLock();
    let calls = 0;
    const save = async () => { calls++; return calls; };

    await lock.run(save);
    await lock.run(save);

    expect(calls).toBe(2);
  });

  it("run_FirstCallThrows_ReleasesLockAndRethrows", async () => {
    const lock = createInFlightLock();
    const failing = async () => { throw new Error("409"); };

    await expect(lock.run(failing)).rejects.toThrow("409");
    expect(lock.isBusy()).toBe(false);

    let ran = false;
    await lock.run(async () => { ran = true; });
    expect(ran).toBe(true);
  });

  it("isBusy_WhilePending_ReturnsTrue", async () => {
    const lock = createInFlightLock();
    const gate = deferred<void>();

    const p = lock.run(() => gate.promise);
    expect(lock.isBusy()).toBe(true);

    gate.resolve();
    await p;
    expect(lock.isBusy()).toBe(false);
  });

  it("run_SynchronousThrow_ReleasesLock", async () => {
    const lock = createInFlightLock();
    const broken = (): Promise<void> => { throw new Error("boom"); };

    await expect(lock.run(broken)).rejects.toThrow("boom");
    expect(lock.isBusy()).toBe(false);
  });
});
