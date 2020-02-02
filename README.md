# Shaman.Reflection
Fast/type-safe reflection for C#.

## Fast string-based reflection
```csharp
// Fast reflection (compiled to IL on first use, no argument boxing/array)
var result = something.InvokeFunction("MethodName", "hello world", 42);

// Static method or static constructor invocation
var instance = someType.InvokeFunction(".ctor", "hello world");
var staticProperty = someType.InvokeFunction("get_InternalProperty");
```

## Fast static-typed reflection
This feature makes it possible to easily wrap non-public types and methods from other assemblies.

Use `Func<TThis, TArgs..., TResult>` for instance methods/properties/fields, and `Func<TArgs..., TResult`> for static ones (and `Action` for setters/void methods).

If an argument or return type is non-public, just use `object` in its `Func` definition.

```csharp
class ExampleWrapper
{
    // Use different _OverloadXx fields to allow multiple overloads of the same method 
    public static Func<Example, int, int, int> SomeMethod_Overload1;
    public static Func<Example, double, double, double> SomeMethod_Overload2;

    // Properties
    public static Action<Example, string> set_Name;
    public static Func<Example, string> get_Name;

    static object _dummy = ReflectionHelper.InitializeWrapper(typeof(ExampleWrapper), typeof(Example));
}
```

Additionally, you can directly use `ReflectionHelper.GetWrapper<>` methods for creating wrappers and storing them where you prefer.

## MakeGenericTypeFast, MakeGenericMethodFast
Every time you call the ordinary `MakeGenericXx` methods, multiple objects are internally allocated (including the `params[] Type` array).

After the initial instantiation of a generic type or method, subsequent calls are performed allocation-free.

```csharp
using Shaman.Runtime;

// Fast generic type instantiation (no intermediate allocations)
Type intType = typeof(int);
Type listOfIntType = typeof(List<>).MakeGenericTypeFast(intType);
 ```
