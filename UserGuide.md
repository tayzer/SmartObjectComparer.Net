# XML Comparison Tool - User Guide

This comprehensive guide will walk you through using the XML Comparison Tool to effectively compare XML files, analyze differences, and generate reports.

## Table of Contents

1. [Introduction](#introduction)
2. [Starting the Application](#starting-the-application)
3. [Interface Overview](#interface-overview)
4. [Basic Comparison Workflow](#basic-comparison-workflow)
5. [Advanced Configuration](#advanced-configuration)
6. [Understanding Comparison Results](#understanding-comparison-results)
7. [Working with Pattern Analysis](#working-with-pattern-analysis)
8. [Exporting and Sharing Results](#exporting-and-sharing-results)
9. [Tips and Best Practices](#tips-and-best-practices)
10. [Troubleshooting](#troubleshooting)

## Introduction

The XML Comparison Tool is designed to help you identify and understand differences between XML files. It's particularly useful for:

- Regression testing of API responses
- Validating XML transformations
- Quality assurance of XML-based data exchange
- Troubleshooting XML differences between environments

The tool provides not only basic difference detection but also advanced pattern analysis to help you understand systemic issues across multiple files.

## Starting the Application

1. From the command line, navigate to the ComparisonTool.Web directory
2. Run the command: `dotnet run`
3. Open your browser and navigate to:
   - https://localhost:7178 (HTTPS)
   - http://localhost:5156 (HTTP)

## Interface Overview

The application's interface is divided into several sections:

- **File Selection Panel** (Step 1): Upload and organize XML files for comparison
- **Configuration Panel** (Step 2): Configure comparison settings and rules
- **Results Overview**: Summary of file comparison results with file pair selection
- **Detailed Differences View**: In-depth visualization of differences for a selected file pair
- **Pattern Analysis Panel**: Cross-file analysis showing common difference patterns (for multiple file comparisons)

## Basic Comparison Workflow

### Step 1: Select Files for Comparison

1. **Select Domain Model**: Choose the appropriate XML schema model from the dropdown (typically "SoapEnvelope" for SOAP XML files)
2. **Upload V1 Files**:
   - Click the "Browse" button in the V1 Files section
   - Select one or more XML files representing your baseline/expected files
   - Files will be sorted by name automatically
3. **Upload V2 Files**:
   - Click the "Browse" button in the V2 Files section
   - Select one or more XML files representing your actual/current files
   - These will be compared against the V1 files in order

> **Note**: Files are paired in order by filename. For example, the first V1 file will be compared with the first V2 file, and so on.

### Step 2: Configure Comparison Settings

1. **Basic Settings**:
   - **Ignore Collection Ordering**: Enable this if the order of items in collections doesn't matter
   - **Ignore String Case**: Enable this for case-insensitive string comparison
   - **Enable Cross-File Pattern Analysis**: Keep this enabled to get insights across multiple file pairs

2. **Property-Specific Rules** (Optional):
   - Click "Configure Properties" to open the property selector
   - Search or browse for properties you want to configure
   - Select a property and choose from the following options:
     - **Ignore this property completely**: The property will be excluded from comparison
     - **Ignore collection ordering** (for collections): Order of items won't affect the comparison
     - **Ignore case sensitivity** (for strings): Case differences will be ignored
   - Click "Add to Ignore List" to apply the rule
   - You can add multiple property rules
   - Click "Close" when finished

3. **Run Comparison**:
   - Click the "Run Comparison" button to start the analysis
   - A loading indicator will appear while the comparison is in progress

## Advanced Configuration

### Configuring Property Rules

The property selector allows you to fine-tune which properties are compared and how:

1. **Finding Properties**:
   - Use the search box to quickly locate properties by name
   - Or browse the full property tree to explore all available properties

2. **Property Rule Options**:
   - **Ignore completely**: The property will be excluded from comparison entirely
   - **Ignore collection ordering**: For arrays or lists, ignore the order of items
   - **Ignore case sensitivity**: For text properties, ignore case differences

3. **Path Syntax for Properties**:
   - Simple properties: `PropertyName`
   - Nested properties: `Parent.Child.Grandchild`
   - Collection items: `Results[*].Description` (affects all items)

4. **Common Properties to Ignore**:
   - Timestamps or date fields that change with each run
   - IDs or unique identifiers that are expected to change
   - Order or sequence numbers in dynamic collections

### Handling Collection Differences

Collections (arrays/lists) can be compared in different ways:

1. **Strict Ordering** (default): Items must appear in the same order to be considered equal
2. **Ignore Ordering**: Items can appear in any order as long as they contain the same values
3. **Ignore Specific Collection**: Configure a specific collection to ignore ordering while keeping strict ordering for others

To configure collection-specific settings:
1. Open the property selector
2. Find the collection property (e.g., `Results` or `Tags`)
3. Select "Ignore collection ordering"
4. Add to ignore list

## Understanding Comparison Results

### File Comparison Results

After running the comparison, you'll see a table of file pairs with:
- V1 filename
- V2 filename
- Status (Equal/Different)
- Number of differences
- View button to see details

### Detailed Differences View

When you select a file pair, you'll see:

1. **Comparison Summary**:
   - Overall status (Equal/Different)
   - Total number of differences
   - Charts showing distribution of differences by category and affected objects

2. **Tabs for Different Views**:
   - **Overview**: Summary charts and key statistics
   - **Common Patterns**: Grouped patterns of differences
   - **By Category**: Differences organized by type (text changes, numeric changes, etc.)
   - **By Object**: Differences organized by the affected XML objects

3. **Detailed Differences Table**:
   - Property paths showing exactly where differences occur
   - V1 values (from baseline/expected files)
   - V2 values (from actual/current files)
   - Toggle between "Show Top 100" and "Show All" for large sets of differences

### Difference Categories

Differences are automatically categorized for easier analysis:

- **Text Content**: Changes in string values
- **Numeric Value**: Changes in numeric values (integers, decimals, etc.)
- **Date/Time**: Changes in date or time values
- **Boolean Value**: Changes in true/false values
- **Collection Item**: Changes within collection items
- **Item Added**: New items added to collections
- **Item Removed**: Items removed from collections
- **Null Value Change**: Changes involving null values

## Working with Pattern Analysis

When comparing multiple file pairs, the Pattern Analysis feature helps identify common patterns and systemic issues:

### Viewing Pattern Analysis

The Pattern Analysis panel shows:

1. **Summary Statistics**:
   - Total file pairs compared
   - Files with differences
   - Total differences found
   - Distribution of differences by category

2. **Common Patterns Across Files**:
   - Property paths that show differences in multiple files
   - Number of files affected by each pattern
   - Total occurrences of each pattern

3. **Common Value Changes**:
   - Specific property value changes that appear in multiple files
   - Shows the actual values changing (from → to)
   - Lists affected files

4. **Similar File Groups**:
   - Groups files with similar difference patterns
   - Helpful for identifying batches of files with the same issues

### Using Pattern Analysis Effectively

- Look for high-occurrence patterns to identify systemic issues
- Pay attention to property paths that appear frequently across files
- Use similar file groups to prioritize investigation of related issues
- Compare common value changes to identify potential data transformation issues

## Exporting and Sharing Results

The tool provides several options for exporting results:

### Export Options

1. **Export Individual Result**:
   - Select a file pair to view its details
   - Click "Export This Result" to generate a markdown report for that pair

2. **Export All Results**:
   - From the file comparison results panel
   - Click "Export All Results" to generate a comprehensive report

3. **Export Pattern Analysis**:
   - From the pattern analysis panel
   - Click "Export Analysis" to create a pattern-focused report

### Report Contents

The generated markdown reports include:

- **Comparison Summary**: Overall statistics and key findings
- **Difference Categories**: Breakdown of differences by type
- **Detailed Differences**: Specific property changes with before/after values
- **Pattern Analysis** (if applicable): Common patterns across files
- **File Grouping** (if applicable): Sets of files with similar issues

### Sharing Reports

The exported markdown files can be:
- Shared with team members via email or collaboration tools
- Converted to PDF or HTML for formal documentation
- Included in test reports or regression analysis
- Used for tracking issues over time

## Tips and Best Practices

### Effective Comparison

1. **Organize Files Logically**:
   - Name files consistently so they pair correctly
   - Group related files together for more meaningful pattern analysis

2. **Start with Default Settings**:
   - Run an initial comparison with default settings
   - Then refine by adding property rules based on the initial results

3. **Iterative Refinement**:
   - Begin with strict comparison (no ignore rules)
   - Gradually add ignore rules for expected or irrelevant differences
   - Re-run comparison to focus on meaningful differences

## Troubleshooting

### Common Issues and Solutions

1. **XML Deserialization Errors**:
   - **Problem**: "Error deserializing XML to type X"
   - **Solution**: Ensure your XML format matches the expected schema. Check for XML syntax errors, missing namespaces, or incorrect root elements.

2. **No Model Error**:
   - **Problem**: "No model registered with name X"
   - **Solution**: Make sure you've selected the correct domain model from the dropdown for your XML format.

3. **File Upload Issues**:
   - **Problem**: Files not loading or processing errors
   - **Solution**: Ensure files are valid XML. Try with smaller files first. Check if files exceed the maximum size limit (10MB by default).

4. **Browser Performance**:
   - **Problem**: UI becomes slow when displaying many differences
   - **Solution**: Use the "Show Top 100" option instead of "Show All" for large difference sets. Export results to markdown for offline analysis.

5. **Inconsistent Results**:
   - **Problem**: Getting different results for the same files
   - **Solution**: Check your ignore rules and configuration settings. Make sure you've applied the same rules for each comparison run.

### Error Messages and Meaning

| Error Message | Likely Cause | Solution |
|---------------|--------------|----------|
| "XML stream cannot be null" | Empty or corrupted file | Check file content and try again |
| "Error occurred while comparing XML files" | General comparison error | Check logs for details, verify XML format |
| "Invalid property path" | Incorrect property configuration | Verify the property path syntax |
| "Maximum differences exceeded" | Too many differences found | Increase MaxDifferences in settings or focus comparison |

### Logging

The application generates logs in the `Logs` directory with the following information:
- Errors and exceptions during comparison
- Performance metrics for large comparisons
- Configuration changes and rule applications

Check these logs when troubleshooting unexpected behavior.

## Advanced Use Cases

### Regression Testing Workflow

To use the tool as part of a regression testing workflow:

1. **Setup Baseline**:
   - Create a folder of "golden" or expected XML responses
   - Document the expected behavior and acceptable differences

2. **Regular Comparison**:
   - After code changes or releases, generate new XML responses
   - Use the comparison tool to identify unexpected differences
   - Export reports for documentation

3. **Configuration Management**:
   - Save and version your comparison configurations for consistency
   - Create specific ignore rule sets for different types of tests