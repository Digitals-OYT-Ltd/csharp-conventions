; Shipped analyzer releases (RS2008 release tracking).
; New rules land in AnalyzerReleases.Unshipped.md and move here when a version ships.

## Release 0.11.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DT00013 | Digitals.Design | Warning | Sequence of steps copied inside one member
DT00014 | Digitals.Design | Warning | Large object initializer outside a factory
DT00015 | Digitals.Design | Warning | Orchestration method over its statement budget
DT00016 | Digitals.Design | Warning | Another object's state set field by field
DT00017 | Digitals.Design | Warning | Result unwrapped inline beside other steps
DT00018 | Digitals.Design | Warning | Infrastructure API called from an orchestration method

## Release 0.10.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DT00010 | Digitals.Naming | Warning | Boolean name must be affirmative
DT00011 | Digitals.Naming | Info | Do not recycle a local for an unrelated value
DT00012 | Digitals.Style | Warning | File name must match the type it declares

## Release 0.8.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DT00009 | Digitals.Style | Warning | Do not use #region

## Release 0.7.0

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DT00005 | Digitals.Testing | Warning | Test name must be Method_Scenario_ExpectedResult. Moved to the Digitals.Testing package, which reaches test projects only. The ID is not reused here.

## Release 0.6.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DT00008 | Digitals.Types | Warning | Do not return a nullable collection

## Release 0.5.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DT00007 | Digitals.Temporal | Warning | Do not implement a custom TimeProvider

## Release 0.4.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DT00003 | Digitals.Http | Warning | Do not construct HttpClient directly
DT00004 | Digitals.Types | Warning | Do not use dynamic
DT00005 | Digitals.Testing | Warning | Test name must be Method_Scenario_ExpectedResult
DT00006 | Digitals.Types | Info | Money must not be double or float

## Release 0.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DT00001 | Digitals.Temporal | Warning | Do not use System.DateTime
DT00002 | Digitals.Temporal | Warning | Do not convert DateTimeOffset to an ambient DateTime
