# Expected JSON Customer Lookup Alternate Contract Fixtures

These fixtures exercise request comparison where endpoint A accepts the canonical SOAP/XML request and endpoint B uses the alternate JSON contract.

## Local Manual Run

1. Start the mock API:

   ```powershell
   dotnet run --project .\ComparisonTool.MockApi\ComparisonTool.MockApi.csproj --urls http://localhost:5055
   ```

2. Start either the web app or the desktop app:

   ```powershell
   dotnet run --project .\ComparisonTool.Web\ComparisonTool.Web.csproj --urls http://localhost:5156
   dotnet run --project .\ComparisonTool.Desktop\ComparisonTool.Desktop.csproj
   ```

3. Open `http://localhost:5156` for web, or use the desktop window.
4. In **Request Comparison**, upload one of the fixture folders below. In desktop, use **Add Request Files** and select the request XML files.
5. Select domain model `ExpectedJsonCustomerLookupResponse`.
6. Enable **Endpoint B uses alternate request/response contract**.
7. Use endpoint A `Local Mock Customer Lookup SOAP`.
8. Use endpoint B `Local Mock Customer Lookup JSON`.
9. Run the comparison.

The `ExpectedJsonCustomerLookupResponse` profile should auto-select the customer lookup SOAP/JSON mock endpoints when the alternate contract toggle is enabled. If endpoint A is `Local Mock A`, the run is using the generic order mock and will fail because it returns `OrderManagementResponse`.

## Fixture Sets

- `manual-mixed`: one equal success (`1001`), one structured difference (`1002`), and one non-success/raw response case (`4000`).
- `duplicate-names`: two folder-relative requests named `lookup.xml`; both produce the same customer-name difference and should display as `alpha/lookup.xml` and `beta/lookup.xml`.

The default alternate-contract ignore rule ignores `ExpectedJsonCustomerLookupResponse.SourceSystem`, so `1001` should compare equal even though endpoint A and endpoint B report different source systems.
