# Readonly references in C# and IL verification. #

The goal of this document is to specify additional IL verification rules that would allow C# code that utilizes readonly references to be formally verifiable.  

NOTE: the rules for `readonly` are completely independent and additive to the rules for ref returning members. 

## C# rules for reference: ##

1)	readonly variables cannot be passed or returned by ordinary reference or used in initialization of an ordinary ref local. 
2)	readonly variables:
	- `readonly` fields when used outside of corresponding constructor.
	- fields of `readonly` value-typed variables. 
	- `in` parameters.
	- `ref readonly` returning members.
	- `ref` ternary expression when either of the branches is a readonly variable.
	- `this` inside a member of a `readonly` struct other than the instance constructor.
3)	readonly variables can be passed or returned by _readonly_ reference or used in initialization of a _readonly_ ref local. 
4) Additional special cases where use of a variable is considered readonly: 
  (mostly affects codegeneration, does not have direct effect on semantics of the language):
	- receiver of an instance member of a `readonly` struct
	- receiver of a value type member that is inherited from a base class (`Enum`, `ValueType`, `Object`...)
	- receiver of an instance member of `Nullable<T>`
	- Controlled-mutability references that are obtained via unboxing or array indexing with `readonly.` prefix can be used as readonly references.

NOTE: C# rules operate with high level language entities such as scopes, expressions, local variables, etc., that do not exist at the level of IL or do not map one-to-one.
The following is the attempt to introduce/adjust verification rules to allow validation of the safety of the readonly references at IL level.

# Verification of readonly references. #

## Readonly managed pointer. ##

CLI needs to add a notion of another transient type - readonly managed pointer. 
*(need to adjust “III.1.8.1.1 Verification algorithm” and in “I.8.7 Assignment compatibility” accordingly)*

NOTE: there are similarities with already existing concept of controlled mutability managed references, however `readonly` is a stronger property than controlled mutability. I.E. a controlled mutability reference is accepted anywhere where readonly reference could be used, but not the other way around.

`readonly references` ⊂ `controlled mutability references` ⊂ `ordinary references`

Readonly managed pointer is a transient type used for verification purposes. When certain conditions are met, managed pointers can be classified as readonly. 

Unlike other managed pointer types, readonly managed pointers can be used:
- as operands of `RET` instruction only if containing method returns _readonly_ references
- as an operand of `LDFLD`, `LDIND`, `LDOBJ` and a source operand of `CPOBJ` 
- as an operand of `LDFLDA` - the resulting pointer is considered _readonly_
- as operands of a method call if corresponding parameter is _readonly_

Parameters of a `CALL` or `CALLVIRT` instruction are considered readonly when they are
- annotated as `in` parameters (see metadata encoding)
- `this` parameters of `readonly` struct members (see metadata encoding)   
- `this` parameters of struct members inherited from base types
- `this` parameters of a member of `Nullable<T>`

Readonly managed pointer may be a result of one of the following:
-	`LDSFLDA`, `LDFLDA` when field is readonly 
-	`LDARG` of an `in` parameter, 
     note: arg0 inside an instance member other than .ctor of a readonly struct is considered an `in` parameter.
-	`CALL`, `CALLVIRT` when the method returns a readonly reference

## Merging stack states ## 
*(add to III.1.8.1.3 Merging stack states)*

Merging a readonly managed pointer with an managed pointer of an ordinary or controlled mutability kind results in a readonly managed pointer.

## Readonly local slots ##
CLI needs to add a notion of a readonly local slots.
-   a local byref slot may be marked as readonly. 
-	`STLOC` of a _readonly_ managed pointer is verifiable only when the target is a readonly slot. 
-	`LDLOC` from a readonly slot produces a readonly managed pointer.

The rationale for introducing readonly locals is to allow for a single pass verification analysis. 
While, in theory, it may be possible that the “readonly” property could be inferred via some form of fixed point flow analysis, it would make verification substantially more complex and expensive and would not retain the single-pass property.


# Metadata encoding # 

## `ref readonly` local slots ##
Byref local slots can be marked as readonly by applying `modopt[System.Runtime.CompilerServices.IsReadOnlyAttribute]` in the local signature.

## `ref readonly` returns ##
When `System.Runtime.CompilerServices.IsReadOnlyAttribute` is applied to the return of a byref returning method, it means that the method returns a readonly reference.

In addition, the result signature of such methods (and only those methods) must have `modreq[System.Runtime.CompilerServices.IsReadOnlyAttribute]`. 

**Motivation**: this is to ensure that existing compilers cannot simply ignore `readonly` when invoking methods with `ref readonly` returns

## `in` parameters ##
When `System.Runtime.CompilerServices.IsReadOnlyAttribute` is applied to a byref parameter, it means that the the parameter is an `in` parameter.

In addition, if the method is *abstract* or *virtual*, then the signature of such parameters (and only such parameters) must have `modreq[System.Runtime.CompilerServices.IsReadOnlyAttribute]`. 

**Motivation**: this is done to ensure that in a case of method overriding/implementing the `in` parameters match.

Same requirements apply to `Invoke` methods in delegates. 

**Motivation**: this is to ensure that existing compilers cannot simply ignore `readonly` when creating or assigning delegates.
 
## `readonly` structs ##
When `System.Runtime.CompilerServices.IsReadOnlyAttribute` is applied to a value type, it means that the the type is a `readonly struct`.


In particular:
-  The identity of the `IsReadOnlyAttribute` type is unimportant. In fact it can be embedded by the compiler in the containing assembly if needed.
-  Applying `IsReadOnlyAttribute` to targets not listed here - to byval local slots, byval returns/parameters, reference types, etc... is reserved for the future use and in scope of this proposal results in verification errors. 

---
NOTE: JIT is free to ignore readonly annotations. However it may use that extra information as an input/hints when performing optimizations.

# Matching readonly constraints when overriding/implementing or assigning delegate values # 
   
**Delegate and method signature compatibility:**
*(need to adjust definitions of “delegate-assignable-to” and “method-signature-compatible-
with” accordingly)*

Signatures that differ in terms of `readonly` are not compatible.

NOTE: The modifiers on parameters and returns are ignored here. That is existing behavior and it is not changed to allow delegate creation regardless of whether target method is virtual and uses modifiers or not. 
 
NOTE: The requirement could be relaxed to allow readonly references be compatible with ordinary references in return position and reverse in parameter positions, if more complex rules can be justified.   
 