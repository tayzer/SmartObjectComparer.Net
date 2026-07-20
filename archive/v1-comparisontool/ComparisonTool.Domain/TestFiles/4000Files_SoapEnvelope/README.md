# 4000 File SoapEnvelope Test Dataset

## Overview
This dataset contains 4000 pairs of SOAP Envelope XML files designed to validate namespace handling, nested collections, and order sensitivity.

## Dataset Characteristics
- **File Count**: 4000 Expected + 4000 Actual = 8000 total files
- **Model**: SoapEnvelope (Envelope → Body → SearchResponse)
- **Size**: Each file ~2-8KB (compact but structured)
- **Differences**: Strategic variations to test order, content, and namespace handling

## Key Differences Between Expected/Actual Files
1. **Report IDs**: Suffix added in actual files
2. **GeneratedOn**: Different timestamps
3. **Scores**: Slight score increase in actual files
4. **Details**: Updated description/status values
5. **Tags**: Additional tag in actual files
6. **RelatedItems**: Reversed order in actual files

## Performance Testing Recommendations

### Ignore Rules to Test
```
GeneratedOn
Results[*].Details.Description
Results[*].Score
RelatedItems[*]
```

Generated on: 2026-04-15 16:22:36
Generator: SoapEnvelope model with namespace-aware serialization
