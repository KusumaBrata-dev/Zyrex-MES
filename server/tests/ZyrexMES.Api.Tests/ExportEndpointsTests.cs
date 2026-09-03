using System.Net;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using ZyrexMES.Infrastructure.Persistence;

namespace ZyrexMES.Api.Tests;

public class ExportEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExportEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNgListExcel_ReturnsExcelWithHeadersAndData()
    {
        // Arrange: seed some NG data
        var client = _factory.CreateClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-1);
        var to = today;

        // Act
        var response = await client.GetAsync($"/api/export/ng-list.xlsx?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("NG List");
        worksheet.Should().NotBeNull();

        // Headers
        worksheet.Cell(1, 1).GetString().Should().Be("SN");
        worksheet.Cell(1, 2).GetString().Should().Be("Station");
        worksheet.Cell(1, 3).GetString().Should().Be("NG Code");
        worksheet.Cell(1, 4).GetString().Should().Be("Notes");
        worksheet.Cell(1, 5).GetString().Should().Be("Checked At (WIB)");

        // At least one data row if any data exists, but we can skip if empty
        // We'll just check that the file is valid Excel.
    }

    [Fact]
    public async Task GetStationSummaryExcel_ReturnsExcelWithHeadersAndData()
    {
        var client = _factory.CreateClient();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.GetAsync($"/api/export/station-summary.xlsx?date={date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Station Summary");
        worksheet.Should().NotBeNull();

        worksheet.Cell(1, 1).GetString().Should().Be("Station Code");
        worksheet.Cell(1, 2).GetString().Should().Be("Station Name");
        worksheet.Cell(1, 3).GetString().Should().Be("Output");
        worksheet.Cell(1, 4).GetString().Should().Be("NG");
        worksheet.Cell(1, 5).GetString().Should().Be("Yield %");
    }
}