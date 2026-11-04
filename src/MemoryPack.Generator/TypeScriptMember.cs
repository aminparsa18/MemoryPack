using Microsoft.CodeAnalysis;

namespace MemoryPack.Generator;

internal class TypeScriptType
{
    public string TypeName { get; set; } = default!;
    public bool IsNullable { get; set; }
    public string DefaultValue { get; set; } = default!;
    public string WriteMethodTemplate { get; set; } = default!;
    public string ReadMethodTemplate { get; set; } = default!;
}

internal class TypeScriptTypeCore
{
    public string TypeName { get; set; } = default!;
    public string DefaultValue { get; set; } = default!;
    public string BinaryOperationMethod { get; set; } = default!;
}

internal class NotSupportedTypeException : Exception
{
    public ITypeSymbol ErrorTypeSymbol { get; }
    public MemberMeta? MemberMeta { get; set; }

    public NotSupportedTypeException(ITypeSymbol typeSymbol)
    {
        this.ErrorTypeSymbol = typeSymbol;
    }
}

public class TypeScriptMember
{
    public MemberMeta Member { get; }
    public string MemberName { get; }
    public string TypeName { get; }
    public string DefaultValue { get; }
    public string WriteMethodTemplate { get; }
    public string ReadMethodTemplate { get; }

    public TypeScriptMember(MemberMeta member, ReferenceSymbols references, TypeScriptGenerateOptions options)
    {
        this.Member = member;
        this.MemberName = options.ConvertPropertyName
            ? Char.ToLowerInvariant(member.Name[0]) + member.Name.Substring(1)
            : member.Name;

        TypeScriptType tsType;
        try
        {
            tsType = ConvertToTypeScriptType(member.MemberType, references, options);
        }
        catch (NotSupportedTypeException ex)
        {
            ex.MemberMeta = member;
            throw;
        }

        this.TypeName = tsType.TypeName;
        this.DefaultValue = tsType.DefaultValue;
        this.WriteMethodTemplate = tsType.WriteMethodTemplate;
        this.ReadMethodTemplate = tsType.ReadMethodTemplate;
    }

    TypeScriptType ConvertToTypeScriptType(ITypeSymbol symbol, ReferenceSymbols references, TypeScriptGenerateOptions options)
    {
        if (symbol.TypeKind == TypeKind.Enum)
        {
            var primitiveType = ConvertFromSymbol(symbol, references, options)!;

            // enum uses self typename(convert to const enum)
            return new TypeScriptType
            {
                TypeName = symbol.Name,
                DefaultValue = primitiveType.DefaultValue,
                IsNullable = symbol.NullableAnnotation == NullableAnnotation.Annotated || symbol.IsReferenceType,
                WriteMethodTemplate = $"writer.write{primitiveType.BinaryOperationMethod}({{0}})",
                ReadMethodTemplate = $"reader.read{primitiveType.BinaryOperationMethod}()"
            };
        }

        if (symbol.TypeKind == TypeKind.Array)
        {
            if (symbol is IArrayTypeSymbol array && array.IsSZArray)
            {
                var elemType = array.ElementType;
                if (elemType.SpecialType == SpecialType.System_Byte)
                {
                    return new TypeScriptType
                    {
                        TypeName = $"Uint8Array | null",
                        DefaultValue = "null",
                        WriteMethodTemplate = $"writer.writeUint8Array({{0}})",
                        ReadMethodTemplate = $"reader.readUint8Array()"
                    };
                }

                var innerType = ConvertToTypeScriptType(elemType, references, options);
                var typeName = innerType.TypeName.Contains("null") ? $"({innerType.TypeName})" : innerType.TypeName;

                var elementWriter = string.Format(innerType.WriteMethodTemplate, "x");
                var elementReader = string.Format(innerType.ReadMethodTemplate);

                return new TypeScriptType
                {
                    TypeName = $"{typeName}[] | null",
                    DefaultValue = "null",
                    WriteMethodTemplate = $"writer.writeArray({{0}}, (writer, x) => {elementWriter})",
                    ReadMethodTemplate = $"reader.readArray(reader => {elementReader})"
                };
            }
        }

        // is collection

        var (collectionKind, collectionSymbol) = ParseTypeScriptCollectionKind(symbol as INamedTypeSymbol, references);
        switch (collectionKind)
        {
            case CollectionKind.Collection:
                {
                    var innerType = ConvertToTypeScriptType(collectionSymbol!.TypeArguments[0], references, options);
                    // same as Array
                    var typeName = innerType.TypeName.Contains("null") ? $"({innerType.TypeName})" : innerType.TypeName;

                    var elementWriter = string.Format(innerType.WriteMethodTemplate, "x");
                    var elementReader = string.Format(innerType.ReadMethodTemplate);

                    return new TypeScriptType
                    {
                        TypeName = $"{typeName}[] | null",
                        DefaultValue = "null",
                        WriteMethodTemplate = $"writer.writeArray({{0}}, (writer, x) => {elementWriter})",
                        ReadMethodTemplate = $"reader.readArray(reader => {elementReader})"
                    };
                }
            case CollectionKind.Set:
                {
                    var innerType = ConvertToTypeScriptType(collectionSymbol!.TypeArguments[0], references, options);
                    var elementWriter = string.Format(innerType.WriteMethodTemplate, "x");
                    var elementReader = string.Format(innerType.ReadMethodTemplate);

                    return new TypeScriptType
                    {
                        TypeName = $"Set<{innerType.TypeName}> | null",
                        DefaultValue = "null",
                        WriteMethodTemplate = $"writer.writeSet({{0}}, (writer, x) => {elementWriter})",
                        ReadMethodTemplate = $"reader.readSet(reader => {elementReader})"
                    };
                }
            case CollectionKind.Dictionary:
                {
                    if (options.DictionaryAsRecord)
                    {
                        var record = TryConvertToRecord(collectionSymbol!.TypeArguments[0], collectionSymbol!.TypeArguments[1], references, options);
                        if (record != null) return record;
                    }

                    var keyType = ConvertToTypeScriptType(collectionSymbol!.TypeArguments[0], references, options);
                    var valueType = ConvertToTypeScriptType(collectionSymbol!.TypeArguments[1], references, options);
                    var keyWriter = string.Format(keyType.WriteMethodTemplate, "x");
                    var keyReader = string.Format(keyType.ReadMethodTemplate);
                    var valueWriter = string.Format(valueType.WriteMethodTemplate, "x");
                    var valueReader = string.Format(valueType.ReadMethodTemplate);

                    return new TypeScriptType
                    {
                        TypeName = $"Map<{keyType.TypeName}, {valueType.TypeName}> | null",
                        DefaultValue = "null",
                        WriteMethodTemplate = $"writer.writeMap({{0}}, (writer, x) => {keyWriter}, (writer, x) => {valueWriter})",
                        ReadMethodTemplate = $"reader.readMap(reader => {keyReader}, reader => {valueReader})"
                    };
                }
            default:
                break;
        }

        if (symbol.TryGetMemoryPackableType(references, out _, out _) || symbol.IsWillImplementMemoryPackUnion(references))
        {
            return new TypeScriptType
            {
                TypeName = $"{symbol.Name} | null",
                DefaultValue = "null",
                WriteMethodTemplate = $"{symbol.Name}.serializeCore(writer, {{0}})",
                ReadMethodTemplate = $"{symbol.Name}.deserializeCore(reader)"
            };
        }

        var isNullable = (symbol is INamedTypeSymbol nts && nts.EqualsUnconstructedGenericType(references.KnownTypes.System_Nullable_T));
        if (isNullable)
        {
            var primitiveType = ConvertFromSymbol(((INamedTypeSymbol)symbol).TypeArguments[0], references, options)!;

            return new TypeScriptType
            {
                TypeName = $"{primitiveType.TypeName} | null",
                DefaultValue = "null",
                WriteMethodTemplate = $"writer.writeNullable{primitiveType.BinaryOperationMethod}({{0}})",
                ReadMethodTemplate = $"reader.readNullable{primitiveType.BinaryOperationMethod}()"
            };
        }

        // others
        {
            var primitiveType = ConvertFromSymbol(symbol, references, options)!;
            return new TypeScriptType
            {
                TypeName = $"{primitiveType.TypeName}",
                DefaultValue = primitiveType.DefaultValue,
                WriteMethodTemplate = $"writer.write{primitiveType.BinaryOperationMethod}({{0}})",
                ReadMethodTemplate = $"reader.read{primitiveType.BinaryOperationMethod}()"
            };
        }
    }

    // MemoryPackGenerator_TypeScriptDictionaryAsRecord: a dictionary whose key maps to a TypeScript
    // string (string, Guid) or number (8-32 bit integers, float, double) becomes `Record<K, V>`.
    // Wire format is identical to the Map form (collection header + key/value pairs). Other key
    // types (bigint, Date, enums, objects) return null and keep the Map form: a bigint/Date cannot
    // be an object key without changing the value, and `Record<SomeEnum, V>` would claim that every
    // enum member is present.
    // string keys and (non-nullable) string values are coalesced to "" when null, so the record is
    // `Record<string, string>` rather than `Record<string, string | null>`.
    TypeScriptType? TryConvertToRecord(ITypeSymbol keySymbol, ITypeSymbol valueSymbol, ReferenceSymbols references, TypeScriptGenerateOptions options)
    {
        string keyTypeName;
        string keyReader;
        string keyWriter;
        if (keySymbol.SpecialType == SpecialType.System_String)
        {
            keyTypeName = "string";
            keyReader = "reader.readString() ?? \"\"";
            keyWriter = "writer.writeString(x)";
        }
        else if (SymbolEqualityComparer.Default.Equals(keySymbol, references.KnownTypes.System_Guid))
        {
            keyTypeName = "string";
            keyReader = "reader.readGuid()";
            keyWriter = "writer.writeGuid(x)";
        }
        else if (keySymbol.TypeKind != TypeKind.Enum && keySymbol.SpecialType is
            SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Single or SpecialType.System_Double)
        {
            var keyType = ConvertToTypeScriptType(keySymbol, references, options);
            keyTypeName = "number";
            keyReader = string.Format(keyType.ReadMethodTemplate);
            // object keys come back from Object.keys as strings
            keyWriter = string.Format(keyType.WriteMethodTemplate, "Number(x)");
        }
        else
        {
            return null;
        }

        var valueType = ConvertToTypeScriptType(valueSymbol, references, options);
        var valueTypeName = valueType.TypeName;
        var valueReader = string.Format(valueType.ReadMethodTemplate);
        var valueWriter = string.Format(valueType.WriteMethodTemplate, "x");

        var isDeclaredNullableString = options.EnableNullableTypes && valueSymbol.NullableAnnotation == NullableAnnotation.Annotated;
        if (valueSymbol.SpecialType == SpecialType.System_String && !isDeclaredNullableString)
        {
            if (keySymbol.SpecialType == SpecialType.System_String)
            {
                // The common string -> string case gets dedicated runtime helpers: the generic
                // readRecord allocates two arrow functions per call, which measured ~4% slower on a
                // decode + consume benchmark (sandbox/TypeScriptBench).
                return new TypeScriptType
                {
                    TypeName = "Record<string, string> | null",
                    DefaultValue = "null",
                    WriteMethodTemplate = "writer.writeStringRecord({0})",
                    ReadMethodTemplate = "reader.readStringRecord()"
                };
            }

            valueTypeName = "string";
            valueReader = "reader.readString() ?? \"\"";
        }

        return new TypeScriptType
        {
            TypeName = $"Record<{keyTypeName}, {valueTypeName}> | null",
            DefaultValue = "null",
            WriteMethodTemplate = $"writer.writeRecord({{0}}, (writer, x) => {keyWriter}, (writer, x) => {valueWriter})",
            ReadMethodTemplate = $"reader.readRecord(reader => {keyReader}, reader => {valueReader})"
        };
    }

    // IReadOnlyList<T>/IReadOnlyCollection<T>/IReadOnlyDictionary<K,V> are serialized by
    // InterfaceReadOnly{Collection,List,Dictionary}Formatter with exactly the same wire format as
    // List<T> (collection header + elements) / Dictionary<K,V> (header + key/value pairs), so they
    // map to the same TypeScript array / Map. They are declared as the interface itself, which does
    // not implement ICollection<T>/IDictionary<K,V> and therefore is not seen by
    // TypeMeta.ParseCollectionKind (shared with the C# emitter, so left untouched).
    static (CollectionKind, INamedTypeSymbol?) ParseTypeScriptCollectionKind(INamedTypeSymbol? symbol, ReferenceSymbols references)
    {
        if (symbol is { IsGenericType: true })
        {
            var known = references.KnownTypes;
            if (symbol.EqualsUnconstructedGenericType(known.System_Collections_Generic_IReadOnlyList_T) ||
                symbol.EqualsUnconstructedGenericType(known.System_Collections_Generic_IReadOnlyCollection_T))
            {
                return (CollectionKind.Collection, symbol);
            }
            if (symbol.EqualsUnconstructedGenericType(known.System_Collections_Generic_IReadOnlyDictionary_T))
            {
                return (CollectionKind.Dictionary, symbol);
            }
        }

        return TypeMeta.ParseCollectionKind(symbol, references);
    }

    TypeScriptTypeCore ConvertFromSymbol(ITypeSymbol typeSymbol, ReferenceSymbols reference, TypeScriptGenerateOptions options)
    {
        var isNullable =
            options.EnableNullableTypes &&
            typeSymbol.IsReferenceType &&
            typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;

        var fromSpecial = ConvertFromSpecialType(typeSymbol.SpecialType, isNullable, options.EnableNullableTypes);
        if (fromSpecial != null) return fromSpecial;

        // + Guid or Enum
        var namedType = typeSymbol as INamedTypeSymbol;
        if (namedType == null) throw new NotSupportedTypeException(typeSymbol);

        if (namedType.TypeKind == TypeKind.Enum)
        {
            var specialType = namedType.EnumUnderlyingType!.SpecialType;
            return ConvertFromSpecialType(specialType, isNullable, options.EnableNullableTypes)!;
        }

        if (SymbolEqualityComparer.Default.Equals(namedType, reference.KnownTypes.System_Guid))
        {
            return new TypeScriptTypeCore
            {
                TypeName = "string",
                DefaultValue = "\"00000000-0000-0000-0000-000000000000\"",
                BinaryOperationMethod = "Guid"
            };
        }

        if (SymbolEqualityComparer.Default.Equals(namedType, reference.KnownTypes.System_DateTimeOffset))
        {
            return new TypeScriptTypeCore
            {
                TypeName = "Date",
                DefaultValue = "new Date(0)",
                BinaryOperationMethod = "DateTimeOffset"
            };
        }

        throw new NotSupportedTypeException(typeSymbol);
    }

    TypeScriptTypeCore? ConvertFromSpecialType(
        SpecialType specialType,
        bool isNullable,
        bool allowNullableTypes)
    {
        // NOTE The function to get the TypeScript type was duplicated in order
        //      to keep the old behavior of the code generator.
        return allowNullableTypes
            ? GetNullableTypesAllowedTypeScriptType(specialType, isNullable)
            : GetNonNullableTypesAllowedTypeScriptType(specialType);
    }

    static TypeScriptTypeCore? GetNonNullableTypesAllowedTypeScriptType(SpecialType specialType)
    {
        string typeName;
        string binaryOperationMethod;
        string defaultValue;

        switch (specialType)
        {
            case SpecialType.System_Boolean:
                typeName = "boolean";
                binaryOperationMethod = "Boolean";
                defaultValue = "false";

                break;

            case SpecialType.System_String:
                typeName = "string | null";
                binaryOperationMethod = "String";
                defaultValue = "null";

                break;

            case SpecialType.System_SByte:
                typeName = "number";
                binaryOperationMethod = "Int8";
                defaultValue = "0";

                break;

            case SpecialType.System_Byte:
                typeName = "number";
                binaryOperationMethod = "Uint8";
                defaultValue = "0";

                break;

            case SpecialType.System_Int16:
                typeName = "number";
                binaryOperationMethod = "Int16";
                defaultValue = "0";

                break;

            case SpecialType.System_UInt16:
                typeName = "number";
                binaryOperationMethod = "Uint16";
                defaultValue = "0";

                break;

            case SpecialType.System_Int32:
                typeName = "number";
                binaryOperationMethod = "Int32";
                defaultValue = "0";

                break;

            case SpecialType.System_UInt32:
                typeName = "number";
                binaryOperationMethod = "Uint32";
                defaultValue = "0";

                break;

            case SpecialType.System_Single:
                typeName = "number";
                binaryOperationMethod = "Float32";
                defaultValue = "0";

                break;

            case SpecialType.System_Double:
                typeName = "number";
                binaryOperationMethod = "Float64";
                defaultValue = "0";

                break;

            case SpecialType.System_Int64:
                typeName = "bigint";
                binaryOperationMethod = "Int64";
                defaultValue = "0n";

                break;

            case SpecialType.System_UInt64:
                typeName = "bigint";
                binaryOperationMethod = "Uint64";
                defaultValue = "0n";

                break;

            case SpecialType.System_DateTime:
                typeName = "Date";
                binaryOperationMethod = "Date";
                defaultValue = "new Date(0)";

                break;

            default:
                return null;
        }

        return new TypeScriptTypeCore
        {
            TypeName = typeName,
            DefaultValue = defaultValue,
            BinaryOperationMethod = binaryOperationMethod
        };
    }

    static TypeScriptTypeCore? GetNullableTypesAllowedTypeScriptType(SpecialType specialType, bool isNullable)
    {
        string typeName;
        string binaryOperationMethod;
        string defaultValue;

        string GetTypeName(string typeName) =>
            isNullable ? $"{typeName} | null" : typeName;

        string GetDefaultValue(string defaultValue) =>
            isNullable ? "null" : defaultValue;

        switch (specialType)
        {
            case SpecialType.System_Boolean:
                typeName = GetTypeName("boolean");
                binaryOperationMethod = "Boolean";
                defaultValue = GetDefaultValue("false");

                break;

            case SpecialType.System_String:
                typeName = GetTypeName("string");
                binaryOperationMethod = "String";
                defaultValue = GetDefaultValue(@"""""");

                break;

            case SpecialType.System_SByte:
                typeName = GetTypeName("number");
                binaryOperationMethod = "Int8";
                defaultValue = GetDefaultValue("0");

                break;

            case SpecialType.System_Byte:
                typeName = GetTypeName("number");
                binaryOperationMethod = "Uint8";
                defaultValue = GetDefaultValue("0");

                break;

            case SpecialType.System_Int16:
                typeName = GetTypeName("number");
                binaryOperationMethod = "Int16";
                defaultValue = GetDefaultValue("0");

                break;

            case SpecialType.System_UInt16:
                typeName = GetTypeName("number");
                binaryOperationMethod = "Uint16";
                defaultValue = GetDefaultValue("0");

                break;

            case SpecialType.System_Int32:
                typeName = GetTypeName("number");
                binaryOperationMethod = "Int32";
                defaultValue = GetDefaultValue("0");

                break;

            case SpecialType.System_UInt32:
                typeName = GetTypeName("number");
                binaryOperationMethod = "Uint32";
                defaultValue = GetDefaultValue("0");

                break;

            case SpecialType.System_Single:
                typeName = GetTypeName("number");
                binaryOperationMethod = "Float32";
                defaultValue = GetDefaultValue("0");

                break;

            case SpecialType.System_Double:
                typeName = GetTypeName("number");
                binaryOperationMethod = "Float64";
                defaultValue = GetDefaultValue("0");

                break;

            case SpecialType.System_Int64:
                typeName = GetTypeName("bigint");
                binaryOperationMethod = "Int64";
                defaultValue = GetDefaultValue("0n");

                break;

            case SpecialType.System_UInt64:
                typeName = GetTypeName("bigint");
                binaryOperationMethod = "Uint64";
                defaultValue = GetDefaultValue("0n");

                break;

            case SpecialType.System_DateTime:
                typeName = GetTypeName("Date");
                binaryOperationMethod = "Date";
                defaultValue = GetDefaultValue("new Date(0)");

                break;

            default:
                return null;
        }

        return new TypeScriptTypeCore
        {
            TypeName = typeName,
            DefaultValue = defaultValue,
            BinaryOperationMethod = binaryOperationMethod
        };
    }
}
