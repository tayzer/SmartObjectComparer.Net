# 4000 File Performance Test Dataset

## Overview
This dataset contains 4000 pairs of ComplexOrderResponse XML files designed to thoroughly test performance optimizations with large file sets and ignore rules.

## Dataset Characteristics
- **File Count**: 4000 Expected + 4000 Actual = 8000 total files
- **Model**: ComplexOrderResponse (60+ classes, 150+ properties)
- **Size**: Each file ~15-30KB (complex nested structures)
- **Differences**: Strategic variations to test ignore rule performance

## Key Differences Between Expected/Actual Files
1. **Version changes**: ApiVersion 2.1.4 → 2.1.5
2. **Status changes**: Processing → Shipped
3. **Regional differences**: US-EAST-1 → US-WEST-2  
4. **Preference changes**: SMS notifications enabled/disabled
5. **Discount differences**: 0 → random discount amounts
6. **User differences**: Different user IDs in audit trail
7. **Timestamp variations**: Different audit and event times
8. **Content updates**: Product descriptions, review content
9. **Validation messages**: Only present in actual files
10. **Points differences**: Loyalty program points variations

## Performance Testing Recommendations

### Ignore Rules to Test
```
// Timestamps (commonly ignored)
Timestamp
OrderData.OrderHistory[*].Timestamp
AuditTrail[*].Timestamp
Metadata.Performance.DatabaseQueryTime

// IDs and audit data
RequestId
OrderData.OrderHistory[*].UserId
AuditTrail[*].UserId

// Dynamic content
OrderData.Items[*].Product.Reviews[*].Content
ValidationMessages[*]

// Regional/version differences  
Metadata.Region
ApiVersion
Metadata.ServerInfo.DeploymentVersion
```

### Expected Performance Improvements
- **Before**: 10+ seconds with 10+ ignore rules
- **After**: <2 seconds with fast filtering optimization
- **Memory**: Significantly reduced MembersToIgnore list size

Generated on: 2025-07-11 16:42:13
Generator: ComplexOrderResponse model with strategic variations
