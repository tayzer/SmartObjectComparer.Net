# CustomerOrder Test Domain

This directory contains a comprehensive test scenario for validating JSON comparison functionality using a realistic e-commerce order domain model.

## Test Domain Model: CustomerOrder

The `CustomerOrder` model represents a complete e-commerce order with:

- **Customer Information**: ID, name, email, VIP status, loyalty points
- **Order Items**: Products with quantities, prices, discounts, and attributes
- **Addresses**: Separate shipping and billing addresses  
- **Payment**: Method, transaction details, processing date
- **Order Status**: Processing, Shipped, Delivered, etc.
- **Metadata**: Notes, tags, total amount

## Test Files

### JSON Test Files
- **`Original.json`**: Base customer order
- **`Modified.json`**: Same order with strategic modifications

### Expected Differences (24 total)

When comparing `Original.json` vs `Modified.json`, the tool should detect these differences:

#### Customer Changes (3 differences)
1. **LastName**: "Smith" → "Johnson"
2. **Email**: "john.smith@email.com" → "john.johnson@email.com"  
3. **IsVip**: false → true
4. **LoyaltyPoints**: 250 → 350

#### Order Item Changes (6 differences)
5. **Laptop Quantity**: 1 → 2
6. **Laptop Discount**: 50.00 → 100.00
7. **Laptop Total**: 1249.99 → 2499.98
8. **Laptop Color Attribute**: "Black" → "Silver"
9. **Laptop RAM Attribute**: "16GB" → "32GB"
10. **New Laptop Attribute Added**: "Storage" = "1TB SSD"

#### New Item Added (7 differences)
11. **New Item**: "KEYBOARD-001" - Mechanical Gaming Keyboard
    - ProductId, ProductName, Category, Quantity, UnitPrice, Discount, Total

#### Address Changes (2 differences)
12. **Shipping Street**: "123 Main Street" → "456 Oak Avenue"
13. **Shipping PostalCode**: "62701" → "62702"

#### Payment Changes (4 differences)
14. **Payment Method**: "CreditCard" → "PayPal"
15. **CardLastFour**: "1234" → null
16. **TransactionId**: "TXN-2024-001-ABC" → "TXN-2024-001-XYZ"
17. **ProcessedDate**: "2024-01-15T10:35:00Z" → "2024-01-15T10:40:00Z"

#### Order Changes (2 differences)
18. **Status**: "Processing" → "Shipped"
19. **TotalAmount**: 1329.98 → 2714.96
20. **Notes**: "Customer requested expedited shipping" → "Customer upgraded to premium shipping"

#### Tags Changes (2 differences)
21. **Removed Tag**: "new-customer"
22. **Added Tags**: "vip-customer", "bulk-order"

## Testing Instructions

### Setup
1. **Register the CustomerOrder Model**:
   ```csharp
   // Add to ServiceCollectionExtensions or registration code
   services.RegisterDomainModel<CustomerOrder>("CustomerOrder");
   ```

### JSON Comparison Test
1. **Load Test Files**: Upload `Original.json` and `Modified.json`
2. **Select Model**: Choose "CustomerOrder" from the model dropdown
3. **Run Comparison**: Execute the comparison
4. **Verify Results**: Should detect approximately 24 differences

### Validation Points

#### ✅ **Format Detection**
- Tool should automatically detect JSON format from file extension
- No manual format selection required

#### ✅ **Deserialization**
- Both files should deserialize successfully to CustomerOrder objects
- No parsing errors or exceptions

#### ✅ **Difference Detection**
- Should detect all 24 expected differences listed above
- No duplicate differences reported
- Clear property path identification

#### ✅ **Collection Handling**
- Properly detect changes within arrays (items, attributes, tags)
- Handle new items added to collections
- Distinguish between collection modifications and new entries

#### ✅ **Data Type Support**
- String changes (names, addresses)
- Numeric changes (quantities, amounts)
- Boolean changes (VIP status)
- Enum changes (payment method, order status)
- DateTime changes (processing dates)
- Null value handling (cardLastFour)

#### ✅ **Nested Object Support**
- Customer object changes
- Address object changes  
- Payment object changes
- Product attribute changes

## Performance Validation

The test should complete within reasonable time limits:
- **Deserialization**: < 100ms per file
- **Comparison**: < 200ms
- **Total Time**: < 500ms

## Integration with Existing Features

This test validates that JSON support integrates properly with:
- ✅ **Caching**: Results should be cached for repeated comparisons
- ✅ **Filtering**: Ignore rules should work with JSON property paths
- ✅ **Analysis**: Pattern analysis should work with JSON differences
- ✅ **UI**: Frontend should handle JSON files in file selection

## XML Equivalent

To fully test format compatibility, you can create equivalent XML files with the same CustomerOrder data structure. The comparison results should be identical regardless of whether the source files are JSON or XML, demonstrating true format-agnostic comparison capability.

## Expected UI Behavior

When testing in the web interface:
- File upload should accept `.json` files
- Model dropdown should show "CustomerOrder" option
- Results should display clearly formatted property paths
- Difference summary should categorize changes appropriately
- Export functionality should work with JSON comparison results

This comprehensive test validates that the JSON support feature works correctly across all aspects of the comparison tool while maintaining backward compatibility with existing XML functionality. 