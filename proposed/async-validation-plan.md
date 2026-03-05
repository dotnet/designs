# Async Validation Support for System.ComponentModel.DataAnnotations

## Problem Statement

The entire `ValidationAttribute` → `GetValidationResult` → `IsValid` chain is synchronous. The ASP.NET Core validation framework (`IValidatableInfo.ValidateAsync`) is async at the orchestration level (recursive traversal of object graphs), but calls `attribute.GetValidationResult()` synchronously at each step. There's no way to write a `ValidationAttribute` that performs async work (e.g., database checks, external service calls).

## Current Architecture

### System.ComponentModel.Annotations (runtime)
- **`ValidationAttribute`** — abstract base:
  - `virtual bool IsValid(object?)` — deprecated, backwards compat
  - `protected virtual ValidationResult? IsValid(object?, ValidationContext)` — recommended override point
  - `GetValidationResult(object?, ValidationContext)` — public entry point (calls `IsValid`)
  - `Validate(object?, ValidationContext)` — throws on failure
  - Uses `_hasBaseIsValid` volatile bool for mutual-recursion override detection
- **`Validator`** — static class, all sync:
  - `TryValidateObject`, `TryValidateProperty`, `TryValidateValue` → return `bool`
  - `ValidateObject`, `ValidateProperty`, `ValidateValue` → throw on failure
  - All call `attribute.GetValidationResult()` internally
- **`IValidatableObject`** — sync interface: `IEnumerable<ValidationResult> Validate(ValidationContext)`

### ASP.NET Core Validation — Minimal APIs (`src/aspnetcore/src/Validation/`)
- **`IValidatableInfo`** — `Task ValidateAsync(object?, ValidateContext, CancellationToken)`
- **`ValidatablePropertyInfo.ValidateAsync`** — calls `attribute.GetValidationResult()` synchronously in a local `ValidateValue()` helper (lines 153-173)
- **`ValidatableTypeInfo.ValidateAsync`** — calls `attribute.GetValidationResult()` synchronously in `ValidateTypeAttributes()` (lines 125-150), also calls `IValidatableObject.Validate()` synchronously
- **`ValidatableParameterInfo.ValidateAsync`** — calls `attribute.GetValidationResult()` synchronously (lines 86-103)
- **Source Generator** — emits `GeneratedValidatablePropertyInfo`/`GeneratedValidatableTypeInfo` subclasses; delegates `GetValidationAttributes()` to a reflection-based `ValidationAttributeCache`

### ASP.NET Core Validation — Blazor (`src/aspnetcore/src/Components/`)
- **`EditContext.Validate()`** — sync `bool`, fires sync `OnValidationRequested` event
- **`EditForm.HandleSubmitAsync()`** — line 204: `var isValid = _editContext.Validate();` with comment: `// This will likely become ValidateAsync later`
- **`EditContextDataAnnotationsExtensions`** — calls `Validator.TryValidateObject()` and `Validator.TryValidateProperty()` synchronously. Also calls `_validatorTypeInfo.ValidateAsync()` but **throws if it doesn't complete synchronously** (line 164-166)
- **Architecture is prepared but blocked**: async validation infrastructure exists but the sync `EventHandler<ValidationRequestedEventArgs>` event model prevents true async

### ASP.NET Core Validation — MVC (`src/aspnetcore/src/Mvc/`)
- **`IModelValidator`** — sync interface: `IEnumerable<ModelValidationResult> Validate(ModelValidationContext)`
- **`DataAnnotationsModelValidator`** — calls `Attribute.GetValidationResult(model, context)` at line 83
- **`ValidationVisitor`** — sync recursive traversal, calls `validators[i].Validate(context)` at line 226
- **`IObjectModelValidator`** — sync: `void Validate(ActionContext, ValidationStateDictionary?, string, object?)`
- **Entirely synchronous** — no async hooks exist

### Microsoft.Extensions.Options Validation (`src/runtime/.../Microsoft.Extensions.Options/`)
- **`IValidateOptions<T>`** — sync interface: `ValidateOptionsResult Validate(string?, TOptions)`
- **`OptionsFactory<T>.Create()`** — sync, iterates `IValidateOptions<T>[]` sequentially
- **`DataAnnotationValidateOptions<T>`** — calls `Validator.TryValidateObject()` synchronously
- **Options source generator** — generates sync `Validate()` methods
- **`ValidateOnStart`/`IStartupValidator`** — sync startup validation
- **Async would be a major architectural shift**: `OptionsFactory.Create()` is sync, lazy options resolution is sync

### Key Call Sites Where Sync `GetValidationResult` Is Used
1. `ValidatablePropertyInfo.cs:84` — RequiredAttribute check
2. `ValidatablePropertyInfo.cs:160` — General attribute validation loop
3. `ValidatableTypeInfo.cs:133` — Type-level attribute validation
4. `ValidatableParameterInfo.cs:75` — RequiredAttribute check
5. `ValidatableParameterInfo.cs:91` — General attribute validation loop
6. `Validator.cs:614` — `TryValidate()` internal helper
7. `Validator.cs:486` — Required attribute check in `GetObjectPropertyValidationErrors`
8. `DataAnnotationValidateOptions.cs:85` — Options validation via `Validator.TryValidateObject`
9. `DataAnnotationsModelValidator.cs:83` — MVC model validation
10. `ValidatableObjectAdapter.cs:42` — MVC IValidatableObject validation
11. `EditContextDataAnnotationsExtensions.cs:91,121` — Blazor form validation

---

## Chosen Approach: Option C — `AsyncValidationAttribute` Deriving from `ValidationAttribute`

Per review feedback, the chosen approach is a new `AsyncValidationAttribute` class that **derives from** `ValidationAttribute`. This provides:
- Clear separation: async-only attributes are a distinct type
- Clean failure: the sync `IsValid` override throws `NotSupportedException` in sync paths
- No silent skipping: since it derives from `ValidationAttribute`, sync `Validator` still discovers it
- No sync-over-async: sync callers get a clear error telling them to use async APIs
  - TODO: Add opt-in to sync-over-async (via ValidationContext and/or AppContext switch?)
- TODO: IAsyncEnumerable API to get validation results ASAP

### Core Design

```csharp
namespace System.ComponentModel.DataAnnotations;

public abstract class AsyncValidationAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        throw new NotSupportedException(
            $"The validation attribute '{GetType().Name}' supports only asynchronous validation. " +
            "Use the async validation APIs (e.g., Validator.TryValidateObjectAsync).");
    }

    // New async override point
    protected abstract ValueTask<ValidationResult?> IsValidAsync(
        object? value,
        ValidationContext validationContext,
        CancellationToken cancellationToken);
}
```


**Key behaviors:**
| Attribute Type | Sync Path (`GetValidationResult`) | Async Path (`GetValidationResultAsync`) |
|---|---|---|
| Traditional `ValidationAttribute` subclass (overrides `IsValid`) | ✅ Works normally | ✅ Default `IsValidAsync` delegates to sync `IsValid` |
| `AsyncValidationAttribute` subclass (overrides `IsValidAsync`) | ❌ Throws `NotSupportedException` with guidance | ✅ Calls `IsValidAsync` |
| `ValidationAttribute` subclass overriding both `IsValid` AND `IsValidAsync` | ✅ Uses `IsValid` | ✅ Uses `IsValidAsync` |

---

## Other Options Considered

### Option A: Virtual `IsValidAsync` Only on `ValidationAttribute` (No Separate Subclass)
Same as above but without the `AsyncValidationAttribute` intermediate class. Users override `IsValidAsync` directly on `ValidationAttribute` and the sync `IsValid` base uses reflection to detect async-only attributes.

**Not chosen because:** Reflection-based override detection is fragile. Option C's override is cleaner and more explicit.

### Option B: Separate `AsyncValidationAttribute` NOT Deriving from `ValidationAttribute`
A completely independent attribute class.

**Not chosen because:** Sync `Validator.TryValidateObject` uses `GetCustomAttributes<ValidationAttribute>()` and would never see it — silent skipping (worst outcome).

### Option D: `IAsyncValidationAttribute` Interface
An opt-in interface that `ValidationAttribute` subclasses can implement.

**Not chosen because:** Less discoverable. Users must know to implement an interface AND inherit `ValidationAttribute`. The subclass approach is more idiomatic for the existing DataAnnotations pattern.

---

## Implementation Plan

### Phase 1: Core `ValidationAttribute` Changes
**Library:** `src/runtime/src/libraries/System.ComponentModel.Annotations/`

#### 1.1 `ValidationAttribute.cs` — Add Async Methods to Base Class
- Add `protected virtual ValueTask<ValidationResult?> IsValidAsync(object?, ValidationContext, CancellationToken)` with default calling sync `IsValid(value, validationContext)` wrapped in `ValueTask`
- Add `public ValueTask<ValidationResult?> GetValidationResultAsync(object?, ValidationContext, CancellationToken)` mirroring `GetValidationResult` but calling `IsValidAsync`
- Use `ValueTask<>` to avoid allocations when the common path is sync (default impl returns `new ValueTask<>(syncResult)`)

#### 1.2 `AsyncValidationAttribute.cs` — New File
```csharp
namespace System.ComponentModel.DataAnnotations;

public abstract class AsyncValidationAttribute : ValidationAttribute
{
    protected AsyncValidationAttribute() { }
    protected AsyncValidationAttribute(string errorMessage) : base(errorMessage) { }
    protected AsyncValidationAttribute(Func<string> errorMessageAccessor) : base(errorMessageAccessor) { }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        throw new NotSupportedException(
            SR.Format(SR.AsyncValidationAttribute_RequiresAsync, GetType().Name));
    }

    // Override base IsValidAsync to make it abstract for subclasses
    protected abstract override ValueTask<ValidationResult?> IsValidAsync(
        object? value,
        ValidationContext validationContext,
        CancellationToken cancellationToken);
}
```

#### 1.3 `Validator.cs` — Add Async Counterparts
Add async versions of all public validation methods:

| Existing Sync Method | New Async Method |
|---------------------|-----------------|
| `TryValidateObject(instance, ctx, results)` | `TryValidateObjectAsync(instance, ctx, results, ct)` |
| `TryValidateObject(instance, ctx, results, validateAll)` | `TryValidateObjectAsync(instance, ctx, results, validateAll, ct)` |
| `TryValidateProperty(value, ctx, results)` | `TryValidatePropertyAsync(value, ctx, results, ct)` |
| `TryValidateValue(value, ctx, results, attrs)` | `TryValidateValueAsync(value, ctx, results, attrs, ct)` |
| `ValidateObject(instance, ctx)` | `ValidateObjectAsync(instance, ctx, ct)` |
| `ValidateObject(instance, ctx, validateAll)` | `ValidateObjectAsync(instance, ctx, validateAll, ct)` |
| `ValidateProperty(value, ctx)` | `ValidatePropertyAsync(value, ctx, ct)` |
| `ValidateValue(value, ctx, attrs)` | `ValidateValueAsync(value, ctx, attrs, ct)` |

Internal async helpers:
- `GetObjectValidationErrorsAsync` — async version of `GetObjectValidationErrors`
- `GetValidationErrorsAsync` — async version of `GetValidationErrors`
- `TryValidateAsync` — async version of `TryValidate`, calls `GetValidationResultAsync`

Sync methods remain unchanged — they will throw if an `AsyncValidationAttribute` is encountered (via the `IsValid` override).

#### 1.4 `IAsyncValidatableObject.cs` — New Interface
```csharp
namespace System.ComponentModel.DataAnnotations;

public interface IAsyncValidatableObject : IValidatableObject
{
    IEnumerable<ValidationResult> IValidatableObject.Validate(ValidationContext validationContext) =>
        throw new NotSupportedException(

    IAsyncEnumerable<ValidationResult> ValidateAsync(
        ValidationContext validationContext,
        CancellationToken cancellationToken = default);
}
```

The async `Validator` methods will check for this interface in Step 3 of validation (after properties and type attributes), mirroring the `IValidatableObject` pattern.

Note: `IAsyncEnumerable<>` allows streaming results. The alternative `Task<IEnumerable<ValidationResult>>` is simpler but less flexible.

#### 1.5 Ref Assembly Updates
Update `ref/System.ComponentModel.Annotations.cs` with all new public API surface:
- `AsyncValidationAttribute` class
- `IAsyncValidatableObject` interface
- All new `Validator` async methods
- `ValidationAttribute.IsValidAsync` and `GetValidationResultAsync`

#### 1.6 Resource Strings
Add new error message to `.resx`:
- `AsyncValidationAttribute_RequiresAsync` — "The validation attribute '{0}' supports only asynchronous validation via IsValidAsync. Use Validator.TryValidateObjectAsync or the equivalent async validation API."

### Phase 2: ASP.NET Core Minimal API Validation Changes
**Library:** `src/aspnetcore/src/Validation/src/`

These classes already have `async Task ValidateAsync(...)` methods. The change is to call `GetValidationResultAsync` instead of `GetValidationResult` within them.

#### 2.1 `ValidatablePropertyInfo.cs`
- **Line 84:** Change `_requiredAttribute.GetValidationResult(...)` to `await _requiredAttribute.GetValidationResultAsync(..., cancellationToken)`. Built-in `RequiredAttribute` is sync, so this completes synchronously via `ValueTask`.
- **Lines 153-173:** Convert the `ValidateValue` local function to an async method. Change `attribute.GetValidationResult(val, context.ValidationContext)` to `await attribute.GetValidationResultAsync(val, context.ValidationContext, cancellationToken)`.

#### 2.2 `ValidatableTypeInfo.cs`
- **`ValidateTypeAttributes` (lines 125-150):** Rename to `ValidateTypeAttributesAsync`, make async, call `await attribute.GetValidationResultAsync(...)` at line 133.
- **`ValidateValidatableObjectInterface` (lines 152-190):** Rename to `ValidateValidatableObjectAsync`. Add `IAsyncValidatableObject` support: when `value is IAsyncValidatableObject asyncValidatable`, use `await foreach (var result in asyncValidatable.ValidateAsync(...))`. Continue supporting existing `IValidatableObject` as fallback.
- **`ValidateAsync` (lines 51-105):** Update to call the renamed async methods.

#### 2.3 `ValidatableParameterInfo.cs`
- **Line 75:** Change RequiredAttribute check to use `await _requiredAttribute.GetValidationResultAsync(..., cancellationToken)`
- **Lines 86-103:** Change attribute validation loop to use `await attribute.GetValidationResultAsync(..., cancellationToken)`

### Phase 3: Blazor Async Validation Support
**Library:** `src/aspnetcore/src/Components/`

Blazor has architecture **partially prepared** for async validation. Key changes:

#### 3.1 `EditContext.cs`
- Add `public async Task<bool> ValidateAsync()` method — async counterpart to `Validate()`
- Add `public event Func<object, ValidationRequestedEventArgs, Task>? OnAsyncValidationRequested;` — async event for validation handlers
- `ValidateAsync()` invokes async handlers, then checks `!GetValidationMessages().Any()`

#### 3.2 `EditForm.cs`
- **Line 204:** Change `var isValid = _editContext.Validate()` to `var isValid = await _editContext.ValidateAsync()` (the existing comment says `// This will likely become ValidateAsync later`)

#### 3.3 `EditContextDataAnnotationsExtensions.cs`
- **`TryValidateTypeInfo` (lines 147-194):** Remove the `throw new InvalidOperationException("Async validation is not supported")` guard at line 166. Instead, properly `await` the `validationTask`.
- Subscribe to `OnAsyncValidationRequested` instead of `OnValidationRequested` for the type-info validation path
- **`OnFieldChanged` handler (lines 79-102):** Add async field validation using `Validator.TryValidatePropertyAsync()`

### Phase 4: MVC Async Validation Support (Future / Lower Priority)
**Library:** `src/aspnetcore/src/Mvc/`

MVC model validation is entirely synchronous. Full async support would require:

#### 4.1 Changes Needed (Large Scope)
- `IModelValidator` — add `ValueTask<IEnumerable<ModelValidationResult>> ValidateAsync(ModelValidationContext, CancellationToken)`
- `DataAnnotationsModelValidator` — implement async variant calling `GetValidationResultAsync`
- `ValidationVisitor` — make `ValidateNode()` async
- `IObjectModelValidator` — add async `ValidateAsync()` methods
- `ValidatableObjectAdapter` — support `IAsyncValidatableObject`

#### 4.2 Recommendation
This is a **large change** with broad impact across the MVC pipeline. Recommend deferring to a follow-up effort. In the interim:
- MVC `DataAnnotationsModelValidator` calls `GetValidationResult()` synchronously
- If an `AsyncValidationAttribute` is used on an MVC model, `GetValidationResult` → `IsValid` → throws `NotSupportedException` with clear message
- This is the **desired behavior**: users get a clear error saying MVC doesn't support async validation yet

### Phase 5: Options Validation (Future / Lower Priority)
**Library:** `src/runtime/src/libraries/Microsoft.Extensions.Options/`

Options validation is synchronous by design (fail-fast at option creation time). Full async support would require:

#### 5.1 Changes Needed (Major Architectural Shift)
- New `IAsyncValidateOptions<T>` interface
- `OptionsFactory<T>` — add `CreateAsync()` or make `Create()` async-aware
- `DataAnnotationValidateOptions<T>` — use `Validator.TryValidateObjectAsync`
- Options source generator — generate async `ValidateAsync()` methods
- `IStartupValidator` / `ValidateOnStart` — async startup validation

#### 5.2 Recommendation
This is a **major architectural shift**. The sync `OptionsFactory.Create()` is deeply embedded in the options pattern. Recommend deferring to a follow-up effort. In the interim:
- `DataAnnotationValidateOptions<T>` calls `Validator.TryValidateObject()` synchronously
- If an `AsyncValidationAttribute` is used on an options class, it throws `NotSupportedException` with clear message
- This is acceptable: options validation typically doesn't need I/O

### Phase 6: Source Generator Updates

#### 6.1 ASP.NET Core Validation Generator (`src/aspnetcore/src/Validation/gen/`)
- The generated `GeneratedValidatablePropertyInfo` and `GeneratedValidatableTypeInfo` inherit from the base classes and only override `GetValidationAttributes()`.
- The `ValidateAsync` methods are inherited from base classes, which we're updating in Phase 2.
- **No emitter changes should be needed.** Verify that no generated code directly calls `GetValidationResult`.

#### 6.2 Options Source Generator (`src/runtime/.../Microsoft.Extensions.Options/gen/`)
- Currently emits sync `Validate()` methods calling `Validator.TryValidateValue()`
- **No changes in this effort** — deferred with Options async support (Phase 5)

### Phase 7: Tests

#### 7.1 Runtime Tests — `AsyncValidationAttribute` & `Validator` Async
- Test that `AsyncValidationAttribute` subclass throws `NotSupportedException` when sync `GetValidationResult` is called
- Test that `AsyncValidationAttribute` subclass works via `GetValidationResultAsync`
- Test that traditional `ValidationAttribute` works in both sync and async paths
- Test `Validator.TryValidateObjectAsync` with mixed sync/async attributes
- Test `Validator.ValidateObjectAsync` throws `ValidationException` correctly
- Test `IAsyncValidatableObject` integration in async `Validator` methods
- Test that `ValueTask` returns synchronously for sync-only attributes (no allocation)

#### 7.2 ASP.NET Core Minimal API Tests
- Update existing `ValidatableTypeInfoTests`, `ValidatableParameterInfoTests`
- Add tests with custom `AsyncValidationAttribute` subclasses working end-to-end
- Test that `ValidatablePropertyInfo.ValidateAsync` properly awaits async attributes

#### 7.3 Blazor Tests
- Test `EditContext.ValidateAsync()` with async validation attributes
- Test that `EditForm` properly awaits async validation
- Test field-level async validation

---

## Design Decisions & Notes

### `ValueTask` vs `Task`
Using `ValueTask<ValidationResult?>` for `IsValidAsync`/`GetValidationResultAsync` because:
- The common case is sync attributes where default `IsValidAsync` returns `new ValueTask<>(syncResult)` with zero allocation
- `AsyncValidationAttribute` subclasses benefit from `ValueTask` pooling
- Aligns with modern BCL async patterns

### Sync Path Behavior
1. **Traditional `ValidationAttribute`**: sync path works normally (unchanged)
2. **`AsyncValidationAttribute`**: sync path throws `NotSupportedException` with clear message pointing to async API (via `IsValid` override)
3. **Never** silently skip async validation
4. **Never** do sync-over-async

### `IAsyncValidatableObject` Returns `IAsyncEnumerable`
Allows streaming results and is aligned with the pattern where validation may involve multiple async checks. If deemed too complex for V1, `Task<IEnumerable<ValidationResult>>` is an acceptable simplification.

### Breaking Changes Assessment
- **Non-breaking for existing attributes**: Default `IsValidAsync` on `ValidationAttribute` delegates to sync, so all existing subclasses work in both sync and async paths
- **Non-breaking for existing sync callers**: `Validator.TryValidateObject` etc. unchanged for sync attributes
- **Intentionally breaking**: Using `AsyncValidationAttribute` with sync `Validator` throws `NotSupportedException` — this is by design, not a bug
- **Binary compatible**: All changes are additive (new virtual method, new class, new interfaces, new static methods)

### Phased Delivery
- **Phase 1-2** (Core + Minimal APIs): The minimum viable async validation story. This alone enables async validation in the new Minimal API validation framework.
- **Phase 3** (Blazor): High value — Blazor forms are a natural fit for async validation (UI-driven, already async). Architecture already partially prepared (existing `ValidateAsync` comment, throw-if-async guard).
- **Phase 4-5** (MVC + Options): Lower priority, larger scope. Defer to follow-up. Both throw clearly when async-only attributes are used.

---

## Files to Modify

### Runtime (`src/runtime/`)
| File | Change |
|------|--------|
| `.../DataAnnotations/ValidationAttribute.cs` | Add `IsValidAsync`, `GetValidationResultAsync` |
| `.../DataAnnotations/AsyncValidationAttribute.cs` | **New file** — overridden sync + abstract async |
| `.../DataAnnotations/Validator.cs` | Add all async method counterparts |
| `.../DataAnnotations/IAsyncValidatableObject.cs` | **New file** |
| `ref/System.ComponentModel.Annotations.cs` | Add public API surface |
| `.../tests/.../ValidationAttributeTests.cs` | Async tests |
| `.../tests/.../ValidatorTests.cs` | Async tests |
| SR strings / .resx | New error messages |

### ASP.NET Core — Minimal APIs (`src/aspnetcore/`)
| File | Change |
|------|--------|
| `.../Validation/src/ValidatablePropertyInfo.cs` | Call `GetValidationResultAsync` |
| `.../Validation/src/ValidatableTypeInfo.cs` | Async type attrs, `IAsyncValidatableObject` support |
| `.../Validation/src/ValidatableParameterInfo.cs` | Call `GetValidationResultAsync` |
| `.../Validation/test/.../ValidatableTypeInfoTests.cs` | Async test updates |
| `.../Validation/test/.../ValidatableParameterInfoTests.cs` | Async test updates |

### ASP.NET Core — Blazor (`src/aspnetcore/`)
| File | Change |
|------|--------|
| `.../Components/Forms/src/EditContext.cs` | Add `ValidateAsync()`, async event |
| `.../Components/Web/src/Forms/EditForm.cs` | Use `ValidateAsync()` |
| `.../Components/Forms/src/EditContextDataAnnotationsExtensions.cs` | Remove async throw guard, await properly |

### No Changes (Deferred)
| File | Reason |
|------|--------|
| ASP.NET Core Validation source generator emitter | Generated classes inherit from base; no direct `GetValidationResult` calls |
| MVC `DataAnnotationsModelValidator`, `ValidationVisitor`, etc. | Deferred to follow-up; throws clearly if async attr used |
| Options `DataAnnotationValidateOptions`, `OptionsFactory`, etc. | Deferred to follow-up; throws clearly if async attr used |
| Options source generator | Deferred with Options async support |
