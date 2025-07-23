# JSON Test Files

This directory contains test JSON files to verify the JSON comparison functionality.

## Files

- **Old.json**: Original version with base values
- **New.json**: Modified version with changed values

## Expected Differences

When comparing these files, the tool should detect:
1. Name changed from "Original Name" to "Modified Name"
2. Description changed from "Original Description" to "Modified Description"

## Usage

These files can be used to test the new JSON support feature by:
1. Selecting JSON format in the comparison tool
2. Uploading both files
3. Running comparison with the ComplexOrderResponse model
4. Verifying that 2 differences are detected

This validates that the JSON deserialization and comparison functionality works correctly alongside the existing XML support. 