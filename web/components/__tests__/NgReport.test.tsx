import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach } from "vitest";
import NgReportPage from "@/app/ng-report/page";

vi.mock("@/lib/api", () => ({
  getNgList: vi.fn(),
}));

import { getNgList } from "@/lib/api";
const getNgListMock = vi.mocked(getNgList);

function pageData(page: number) {
  return {
    total: 30,
    page,
    items: [
      {
        sn: `SN-P${page}`,
        stationCode: "ST-05",
        ngCode: "NG-SCRATCH",
        notes: `defect on page ${page}`,
        checkedAtUtc: "2026-08-26T04:30:00Z",
      },
    ],
  };
}

describe("NgReportPage", () => {
  beforeEach(() => {
    getNgListMock.mockReset();
    // Echo the requested page back so paging assertions are deterministic.
    getNgListMock.mockImplementation((p) => Promise.resolve(pageData(p.page ?? 1)));
  });

  it("renders rows and fetches page 1 with today's WIB date by default", async () => {
    render(<NgReportPage />);

    await waitFor(() => expect(screen.getByText("SN-P1")).toBeInTheDocument());
    expect(screen.getByText("NG-SCRATCH")).toBeInTheDocument();
    expect(screen.getByText("defect on page 1")).toBeInTheDocument();
    const first = getNgListMock.mock.calls[0][0];
    expect(first.page).toBe(1);
    expect(first.pageSize).toBe(25);
    expect(first.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it("Next increments the requested page; Prev goes back", async () => {
    render(<NgReportPage />);
    await waitFor(() => expect(getNgListMock).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByRole("button", { name: /Next/ }));
    await waitFor(() => expect(getNgListMock).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })));

    getNgListMock.mockResolvedValue(pageData(2));
    await waitFor(() => expect(screen.getByText("SN-P2")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: /Prev/ }));
    await waitFor(() => expect(getNgListMock).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1 })));
  });

  it("changing the date refetches with the new date and resets to page 1", async () => {
    render(<NgReportPage />);
    await waitFor(() => expect(getNgListMock).toHaveBeenCalledTimes(1));

    // Walk to page 2 first.
    fireEvent.click(screen.getByRole("button", { name: /Next/ }));
    await waitFor(() => expect(getNgListMock).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })));

    fireEvent.change(screen.getByLabelText(/Date/i), { target: { value: "2026-08-25" } });
    await waitFor(() =>
      expect(getNgListMock).toHaveBeenLastCalledWith(
        expect.objectContaining({ date: expect.stringContaining("2026-08-25"), page: 1 }),
      ),
    );
  });
});
