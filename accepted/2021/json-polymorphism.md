# System.Text.Json polymorphic serialization

**Owner** [Eirik Tsarpalis](https://github.com/eiriktsarpalis)

This documents describes the proposed design for extending [polymorphism support](https://github.com/dotnet/runtime/issues/45189) in System.Text.Json.

[Draft Implementation PR](https://github.com/dotnet/runtime/pull/53882).

## Background

By default, System.Text.Json will serialize a value using a converter derived from its declared type,
regardless of what the runtime type of the value might be. This behavior is in line with the
Liskov substitution principle, in that the serialization contract is unique (or "monomorphic") for a given type `T`,
regardless of what subtype of `T` we end up serializing at runtime.

A notable exception to this rule is members of type `object`, in which case the runtime type of the value
is looked up and serialization is dispatched to the converter corresponding to that runtime type.
This is an instance of _polymorphic serialization_, in the sense that the schema might vary depending on
the runtime type a given `object` instance might have. 

Conversely, in _polymorphic deserialization_ the runtime type of a deserialized value might vary depending on the
shape of the input encoding. Currently, System.Text.Json does not offer any form of support for polymorphic 
deserialization.

We have received a number of user requests to add polymorphic serialization and deserialization support 
to System.Text.Json. This can be a useful feature in domains where exporting type hierarchies is desirable,
for example when serializing tree-like data structures or discriminated unions.

It should be noted however that polymorphic serialization comes with a few security risks:

* Polymorphic serialization applied indiscriminately can result in unintended data leaks, 
  since properties of unexpected derived types may end up written on the wire.
* Polymorphic deserialization can be vulnerable when deserializing untrusted data,
  in certain cases leading to remote code execution attacks.

## Introduction

The proposed design for polymorphic serialization in System.Text.Json can be split into two
largely orthogonal features:

1.  Simple Polymorphic serialization: extends the existing serialization infrastructure for `object` types to 
    arbitrary classes that can be specified by the user. It trivially dispatches to the converter corresponding 
    to the runtime type without emitting any metadata on the wire and does not provide any provision for 
    polymorphic deserialization.
2.  Polymorphism with type discriminators ("tagged polymorphism"): classes can be serialized and deserialized
    polymorphically by emitting a type discriminator ("tag") on the wire. Users must explicitly associate each
    supported subtype of a given declared type with a string identifier.

## Simple Polymorphic Serialization

Consider the following type hierarchy:
```csharp
public class Foo
{
    public int A { get; set; }
}

public class Bar : Foo
{
    public int B { get; set; }
}

public class Baz : Bar
{
    public int C { get; set; }
}
```
Currently, when serializing a `Bar` instance as type `Foo`
the serializer will apply the JSON schema derived from the type `Foo`:
```csharp
Foo foo1 = new Foo { A = 1 };
Foo foo2 = new Bar { A = 1, B = 2 };
Foo foo3 = new Baz { A = 1, B = 2, C = 3 };

JsonSerializer.Serialize<Foo>(foo1); // { "A" : 1 }
JsonSerializer.Serialize<Foo>(foo2); // { "A" : 1 }
JsonSerializer.Serialize<Foo>(foo3); // { "A" : 1 }
```
Under the new proposal we can change this behaviour by annotating 
the base class (or interface) with the `JsonPolymorphicType` attribute:
```csharp
[JsonPolymorphicType]
public class Foo
{
  ...
}
```
which will result in the above values now being serialized as follows:
```csharp
JsonSerializer.Serialize<Foo>(foo1); // { "A" : 1 }
JsonSerializer.Serialize<Foo>(foo2); // { "A" : 1, "B" : 2 }
JsonSerializer.Serialize<Foo>(foo3); // { "A" : 1, "B" : 2, "C" : 3 }
```
Note that the `JsonPolymorphicType` attribute is not inherited by derived types.
In the above example `Bar` inherits from `Foo` yet is not polymorphic in its own right:
```csharp
Bar bar = new Baz { A = 1, B = 2, C = 3 };
JsonSerializer.Serialize<Bar>(bar); // { "A" : 1, "B" : 2 }
```
If annotating the base class with an attribute is not possible,
polymorphism can alternatively be opted in for a type using the
new `JsonSerializerOptions.SupportedPolymorphicTypes` predicate:
```csharp
public class JsonSerializerOptions
{
  public Func<Type, bool> SupportedPolymorphicTypes { get; set; }
}
```
Applied to the example above:
```csharp
var options = new JsonSerializerOptions { SupportedPolymorphicTypes = type => type == typeof(Foo) };
JsonSerializer.Serialize<Foo>(foo1, options); // { "A" : 1, "B" : 2 }
JsonSerializer.Serialize<Foo>(foo2, options); // { "A" : 1, "B" : 2, "C" : 3 }
```
It is always possible to use this setting to enable polymorphism _for every_ serialized type:
```csharp
var options = new JsonSerializerOptions { SupportedPolymorphicTypes = _ => true };

// `options` treats both `Foo` and `Bar` members as polymorphic
Baz baz = new Baz { A = 1, B = 2, C = 3 };
JsonSerializer.Serialize<Foo>(baz, options); // { "A" : 1, "B" : 2, "C" : 3 }
JsonSerializer.Serialize<Bar>(baz, options); // { "A" : 1, "B" : 2, "C" : 3 }
```
As mentioned previously, this feature provides no provision for deserialization.
If deserialization is a requirement, users would need to opt for the
polymorphic serialization with type discriminators feature.

## Polymorphism with type discriminators

This feature allows users to opt in to polymorphic serialization for a given type
by associating string identifiers with particular subtypes in the hierarchy.
These identifiers are written to the wire so this brand of polymorphism is roundtrippable.

At the core of the design is the introduction of `JsonKnownType` attribute that can
be applied to type hierarchies like so:
```csharp
[JsonKnownType(typeof(Derived1), "derived1")]
[JsonKnownType(typeof(Derived2), "derived2")]
public class Base
{
    public int X { get; set; }
}

public class Derived1 : Base
{
    public int Y { get; set; }
}

public class Derived2 : Base
{
    public int Z { get; set; }
}
```
This allows roundtrippable polymorphic serialization using the following schema:
```csharp
var json1 = JsonSerializer.Serialize<Base>(new Derived1()); // { "$type" : "derived1", "X" : 0, "Y" : 0 }
var json2 = JsonSerializer.Serialize<Base>(new Derived2()); // { "$type" : "derived2", "X" : 0, "Z" : 0 }

JsonSerializer.Deserialize<Base>(json1); // uses Derived1 as runtime type
JsonSerializer.Deserialize<Base>(json2); // uses Derived2 as runtime type
```
Alternatively, users can specify known type configuration using the
`JsonSerializerOptions.TypeDiscriminatorConfigurations` property:
```csharp
public class JsonSerializerOptions
{
    public IList<TypeDiscriminatorConfiguration> TypeDiscriminatorConfigurations { get; }
}
```
which can be used as follows:
```csharp
var options = new JsonSerializerOptions
{
    TypeDiscriminatorConfigurations =
    {
        new TypeDiscriminatorConfiguration<Base>()
          .WithKnownType<Derived1>("derived1")
          .WithKnownType<Derived2>("derived2")
    }
};
```
or alternatively
```csharp
var options = new JsonSerializerOptions
{
    TypeDiscriminatorConfigurations =
    {
        new TypeDiscriminatorConfiguration(typeof(Base))
          .WithKnownType(typeof(Derived1), "derived1")
          .WithKnownType(typeof(Derived2), "derived2")
    }
};
```

### Open Questions

The type discriminator semantics could be implemented following two possible alternatives,
which for the purposes of this document I will be calling "strict mode" and "lax mode".
Each approach comes with its own sets of trade-offs.

#### Strict mode

"Strict mode" requires that any runtime type used during serialization must explicitly specify a type discriminator.
For example:
```csharp
[JsonKnownType(typeof(Derived1),"derived1")]
[JsonKnownType(typeof(Derived2),"derived2")]
public class Base { }

public class Derived1 : Base { }
public class Derived2 : Base { }
public class Derived3 : Base { }

public class OtherDerived1 : Derived1 { }

JsonSerializer.Serialize<Base>(new Derived1()); // { "$type" : "derived1" }
JsonSerializer.Serialize<Base>(new Derived2()); // { "$type" : "derived2" }
JsonSerializer.Serialize<Base>(new Derived3()); // throws NotSupportedException
JsonSerializer.Serialize<Base>(new OtherDerived1()); // throws NotSupportedException
JsonSerializer.Serialize<Base>(new Base()); // throws NotSupportedException
```
Any runtime type that is not associated with a type discriminator will be rejected, 
including instances of the base type itself. This approach has a few drawbacks:

* Does not work well with open hierarchies: any new derived types will have to be explicitly opted in.
* Each runtime type must use a separate type identifier.
* Interfaces or abstract classes cannot specify type discriminators.

#### Lax mode

"Lax mode" as the name suggests is more permissive, and runtime types without discriminators 
are serialized using the nearest type ancestor that does specify a discriminator. 
Using the previous example:
```csharp
[JsonKnownType(typeof(Derived1),"derived1")]
[JsonKnownType(typeof(Derived2),"derived2")]
public class Base { }

public class Derived1 : Base { }
public class Derived2 : Base { }
public class Derived3 : Base { }

public class OtherDerived1 : Derived1 { }

JsonSerializer.Serialize<Base>(new Derived1()); // { "$type" : "derived1" }
JsonSerializer.Serialize<Base>(new Derived2()); // { "$type" : "derived2" }
JsonSerializer.Serialize<Base>(new Derived3()); // { } serialized as `Base`
JsonSerializer.Serialize<Base>(new OtherDerived1()); // { "$type" : "derived1" } inherits schema from `Derived1`
JsonSerializer.Serialize<Base>(new Base()); // { } serialized as `Base`
```
This approach is more flexible and supports interface and abstract type hierarchies:
```csharp
[JsonKnownType(typeof(Foo), "foo")]
[JsonKnownType(typeof(IBar), "bar")]
public interface IFoo { }
public abstract class Foo : IFoo { }
public interface IBar : IFoo { }

public class FooImpl : Foo {}

JsonSerializer.Serialize<IFoo>(new FooImpl()); // { "$type" : "foo" }
```
However it does come with its own set of problems:
```csharp
[JsonKnownType(typeof(Foo), "foo")]
[JsonKnownType(typeof(IBar), "bar")]
public interface IFoo { }
public class Foo : IFoo { }
public interface IBar : IFoo { }

public Baz : Foo, IBar { }

JsonSerializer.Serialize<IFoo>(new Baz()); // diamond ambiguity, could either be "foo" or "bar",
                                           // throws NotSupportedException.
```
