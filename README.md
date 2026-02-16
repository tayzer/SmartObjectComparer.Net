    # Comparison Tool

## Overview

The Comparison Tool is an advanced Expected/Actual testing platform designed to intelligently compare two sets of results, enabling testers to efficiently categorize, analyze, and identify common differences across large datasets. Unlike traditional JSON or XML comparison utilities, this tool leverages in-memory processing based on domain models, providing significantly more sophisticated analysis capabilities and actionable insights.

## Key Features

### 🔍 **Smart Domain-Model-Based Comparison**
- **In-Memory Processing**: Operates directly on domain objects rather than raw text/XML, enabling semantic understanding of data structures
- **Type-Aware Analysis**: Understands data types, relationships, and business logic embedded in your domain models
- **Configurable Comparison Rules**: Define custom ignore rules, collection ordering preferences, and property-specific comparison logic

### 📊 **Multi-Layered Analysis Engine**

#### **Pattern Analysis**
- Identifies recurring differences across file pairs
- Groups similar differences for easier review
- Provides frequency analysis and consistency metrics

#### **Semantic Analysis** 
- Groups differences by business meaning and context
- Identifies logical patterns beyond simple structural differences
- Helps understand the business impact of changes

#### **Structural Analysis**
- Detects missing collection elements and properties
- Identifies element ordering differences
- Analyzes structural consistency across datasets

#### **Enhanced Structural Analysis** ⭐
- **Critical Element Detection**: Prioritizes business-critical missing elements
- **Human-Readable Descriptions**: Provides plain-English explanations of differences
- **Actionable Recommendations**: Suggests specific remediation steps
- **Advanced Categorization**: Distinguishes between different types of structural changes

### 🚀 **Performance & Scalability**
- **Batch Processing**: Handle hundreds of file pairs efficiently
- **Progress Tracking**: Real-time progress reporting for long-running comparisons
- **Memory Optimization**: Intelligent memory management for large datasets
- **Cancellable Operations**: Interrupt long-running processes when needed

### 📁 **Flexible Input Methods**
- **Directory Comparison**: Compare entire folders of files
- **File Upload**: Upload specific file sets for comparison
- **Multiple Formats**: Support for various file formats through configurable deserialization

## Why Choose This Tool Over Simple Comparers?

| Feature | Traditional JSON/XML Comparers | Comparison Tool |
|---------|--------------------------------|-----------------|
| **Understanding** | Text-based, syntax-aware only | Domain-model-aware, semantic understanding |
| **Analysis Depth** | Shows what changed | Explains why it matters and what to do |
| **Pattern Recognition** | Manual pattern identification | Automated pattern detection and grouping |
| **Business Context** | Raw technical differences | Business-meaningful categorization |
| **Scale Handling** | Limited to single file pairs | Batch processing with intelligent aggregation |
| **Actionability** | Lists differences | Provides prioritized recommendations |

## Use Cases

### **Quality Assurance Testing**
- Compare API response sets before and after code changes
- Validate data migration results across large datasets
- Expected/Actual test different algorithm implementations

### **Release Validation**
- Ensure system behavior consistency across versions
- Identify unintended side effects in large system changes
- Validate configuration changes across environments

### **Data Analysis**
- Compare dataset outputs from different processing pipelines
- Analyze the impact of parameter changes on results
- Identify systematic differences in data processing

## Analysis Types

### 1. **Critical Element Analysis**
Identifies business-critical properties that are missing or changed, helping teams prioritize their review efforts on the most impactful differences.

### 2. **Pattern Frequency Analysis**
Groups similar differences together, showing how often specific patterns occur across your dataset, making it easy to spot systematic issues.

### 3. **Semantic Grouping**
Organizes differences by business meaning rather than technical structure, helping non-technical stakeholders understand the impact of changes.

### 4. **Collection Analysis**
Specialized analysis for collections and arrays, detecting missing elements, ordering changes, and structural modifications.

### 5. **Value Difference Tracking**
Tracks consistent value changes across properties, helping identify configuration differences or systematic data transformations.

## Getting Started

### Prerequisites
- .NET 8.0 or later
- Web browser (for the UI)
- Domain model assemblies for your data types

### **Basic Usage**

#### Web UI

1. **Configure Domain Models**: Register your domain models for deserialization
2. **Set Comparison Rules**: Define ignore rules and comparison preferences
3. **Load Data**: Upload files or specify directories to compare
4. **Run Analysis**: Execute comparison with desired analysis types enabled
5. **Review Results**: Use the interactive UI to explore patterns and differences

#### CLI

The ComparisonTool CLI lets you run folder and request comparisons from the command line without hosting the web UI.

**Install / Build:**
```bash
dotnet build ComparisonTool.Cli/ComparisonTool.Cli.csproj -c Release
```

**Folder comparison** — compare two directories of XML/JSON files:
```bash
comparisontool folder <expected-dir> <actual-dir> -m ComplexOrderResponse \
  -f Console Json Markdown -o ./reports
```

**Request comparison** — fire requests at two endpoints and diff the responses (with ignore rules + content-type override):
```bash
comparisontool request <request-dir> \
  -a https://host-a/api/endpoint \
  -b https://host-b/api/endpoint \
  -m ComplexOrderResponse -c 32 --timeout 60000 \
  --ignore-rules ./ignore-rules.json \
  --content-type application/json \
  --ignore-collection-order --ignore-namespaces \
  -f Console Json -o ./reports
```

**Ignore rules JSON** (array of `IgnoreRuleDto`):
```json
[
  {
    "propertyPath": "Order.Id",
    "ignoreCompletely": true,
    "ignoreCollectionOrder": false
  },
  {
    "propertyPath": "Order.Items",
    "ignoreCompletely": false,
    "ignoreCollectionOrder": true
  }
]
```

Run `comparisontool --help`, `comparisontool folder --help`, or `comparisontool request --help` for full option details.

**Exit codes:**
| Code | Meaning |
|------|---------|
| 0 | All pairs are equal |
| 1 | Error (bad input, runtime failure) |
| 2 | Comparison completed with differences |

**Configuration:** The CLI reads `appsettings.json` from its output directory (same sections as the web host: `ComparisonSettings`, `RequestComparison`). Override individual settings via environment variables prefixed with `CT_`.

### Example Workflow
```
1. Enable Enhanced Structural Analysis
2. Upload Expected and Actual result sets
3. Configure ignore rules for timestamps/IDs
4. Run comparison
5. Review Critical Elements first
6. Export detailed reports for documentation
```

## Architecture

### **Core Components**
- **Comparison Engine**: High-performance in-memory comparison logic
- **Analysis Pipeline**: Pluggable analysis modules for different insight types
- **Domain Model Integration**: Reflection-based model understanding
- **Web UI**: Modern Blazor-based interface for interactive analysis

### **Analysis Pipeline**
1. **Deserialization**: Convert files to domain objects
2. **Comparison**: Deep object comparison with configurable rules
3. **Pattern Detection**: Identify recurring difference patterns
4. **Semantic Analysis**: Group differences by business meaning
5. **Report Generation**: Create actionable insights and recommendations

## Benefits

- **Faster Reviews**: Automated pattern detection reduces manual analysis time
- **Better Coverage**: Systematic analysis ensures no critical differences are missed
- **Actionable Insights**: Clear recommendations for addressing identified issues
- **Team Collaboration**: Shareable reports and exportable analysis results
- **Confidence**: Comprehensive validation before production releases

## Technical Advantages

- **Type Safety**: Leverages C# type system for robust comparisons
- **Extensibility**: Plugin architecture for custom analysis modules
- **Performance**: Optimized for large-scale batch processing
- **Reliability**: Comprehensive error handling and progress tracking

---

*The Comparison Tool transforms the tedious process of Expected/Actual testing large datasets into an efficient, intelligent analysis workflow that provides actionable insights for development and QA teams.* 