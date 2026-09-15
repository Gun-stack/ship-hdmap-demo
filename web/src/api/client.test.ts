import { describe, it, expect, vi, beforeEach } from "vitest";
import { api, ApiError } from "./client";

function mockFetch(status: number, body: unknown) {
  return vi.fn(async (..._args: unknown[]) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } }));
}

describe("api client", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("lists features with query params", async () => {
    const f = mockFetch(200, [{ id: "LM-0001", layer: "LM", kind: "apriltag", geometry: { type: "Point", coordinates: [1, 2, 3] }, props: {} }]);
    vi.stubGlobal("fetch", f);
    const list = await api.listFeatures("ds1", { deck: "D3", layer: "LM" });
    expect(list[0].id).toBe("LM-0001");
    expect(f.mock.calls[0][0]).toBe("/api/datasets/ds1/features?deck=D3&layer=LM");
  });

  it("throws ApiError with field on 400", async () => {
    vi.stubGlobal("fetch", mockFetch(400, { status: 400, error: "Bad Request", message: "layer must be one of", field: "layer" }));
    await expect(api.createFeature("ds1", { layer: "LM", kind: "x", geometry: { type: "Point", coordinates: [0, 0, 0] } }))
      .rejects.toMatchObject({ status: 400, field: "layer" } satisfies Partial<ApiError>);
  });

  it("delete returns void on 204", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 204 })));
    await expect(api.deleteFeature("ds1", "LM-0001")).resolves.toBeUndefined();
  });
});
