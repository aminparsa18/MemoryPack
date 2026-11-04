#if NET7_0_OR_GREATER

using MemoryPack.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace MemoryPack.Tests;

public partial class GeneratorDiagnosticsTest
{
    void Compile2(int id, string code, bool allowMultipleError = false)
    {
        var (_, diagnostics) = CSharpGeneratorRunner.RunGenerator(code, options: new TypeScriptOptionProvider());
        if (!allowMultipleError)
        {
            diagnostics.Length.Should().Be(1);
            diagnostics[0].Id.Should().Be("MEMPACK" + id.ToString("000"));
        }
        else
        {
            diagnostics.Select(x => x.Id).Should().Contain("MEMPACK" + id.ToString("000"));
        }
    }

    string CompileAndRead(string code, string fileName, bool enableNullableTypes = true, bool dictionaryAsRecord = false)
    {
        var outputDir = Path.GetTempPath();
        var optionProvider = new TypeScriptOptionProvider();

        optionProvider["build_property.MemoryPackGenerator_TypeScriptOutputDirectory"] = outputDir;
        optionProvider["build_property.MemoryPackGenerator_TypeScriptEnableNullableTypes"] = enableNullableTypes ? "true" : "false";
        if (dictionaryAsRecord)
        {
            optionProvider["build_property.MemoryPackGenerator_TypeScriptDictionaryAsRecord"] = "true";
        }

        CSharpGeneratorRunner.RunGenerator(code, options: optionProvider);

        var outputFilePath = Path.Combine(outputDir, fileName);

        return File.ReadAllText(outputFilePath);
    }

    [Fact]
    public void MEMPACK027_GenerateTypeScriptMustBeMemoryPackable()
    {
        Compile2(27, """
using MemoryPack;

[GenerateTypeScript]
public class Hoge
{
}
""");
    }

    [Fact]
    public void MEMPACK028_GenerateTypeScriptOnlyAllowsGenerateTypeObject()
    {
        Compile2(28, """
using MemoryPack;

[MemoryPackable(GenerateType.Collection)]
[GenerateTypeScript]
public partial class Hoge : System.Collections.Generic.List<int>
{
}
""");
    }

    [Fact]
    public void MEMPACK029_GenerateTypeScriptDoesNotAllowGenerics()
    {
        Compile2(29, """
using MemoryPack;

[MemoryPackable]
[GenerateTypeScript]
public partial class Hoge<T>
{
}
""", true);
    }

    [Fact]
    public void MEMPACK030_GenerateTypeScriptDoesNotAllowLongEnum()
    {
        Compile2(30, """
using MemoryPack;

[MemoryPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public LongEnum MyProperty { get; set; }
}

public enum LongEnum : long
{

}
""", true);

        Compile2(30, """
using MemoryPack;

[MemoryPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public LongEnum[] MyProperty { get; set; }
}

public enum LongEnum : ulong
{

}
""", true);

        Compile2(30, """
using MemoryPack;

[MemoryPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public LongEnum? MyProperty { get; set; }
}

public enum LongEnum : long
{

}
""", true);

        Compile2(30, """
using MemoryPack;

[MemoryPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public System.Collections.Generic.List<LongEnum> MyProperty { get; set; }
}

public enum LongEnum : long
{

}
""", true);
    }

    [Fact]
    public void MEMPACK031_GenerateTypeScriptNotSupportedType()
    {
        Compile2(31, """
using MemoryPack;

[MemoryPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public System.Version MyProperty { get; set; }
}
""", true);
    }

    [Fact]
    public void MEMPACK032_GenerateTypeScriptNotSupportedType()
    {
        Compile2(32, """
using MemoryPack;

[MemoryPackable]
[GenerateTypeScript]
public partial class Hoge
{
    [Utf8StringFormatter]
    public string MyProperty { get; set; }
}
""", true);
    }

    [Fact]
    public void GenerateTypeScriptNullableReferenceTypes()
    {
        var generatedCode = CompileAndRead(
            """
            using MemoryPack;

            [MemoryPackable]
            [GenerateTypeScript]
            public partial record FullName
            {
                public string FirstName { get; init; }
                public string? MiddleName { get; init; }
                public string LastName { get; init; }
            }
            """,
            "FullName.ts");

        generatedCode.Should().Contain(
            """
            export class FullName {
                firstName: string;
                middleName: string | null;
                lastName: string;

                constructor() {
                    this.firstName = "";
                    this.middleName = null;
                    this.lastName = "";
            """);
    }

    [Fact]
    public void GenerateTypeScriptDateTimeOffset()
    {
        // DateTimeOffset used to throw MEMPACK031 (not supported type in typescript generation).
        var generatedCode = CompileAndRead(
            """
            using System;
            using MemoryPack;

            [MemoryPackable]
            [GenerateTypeScript]
            public partial class Hoge
            {
                public DateTimeOffset CreatedAt { get; set; }
                public DateTimeOffset? UpdatedAt { get; set; }
            }
            """,
            "Hoge.ts");

        generatedCode.Should().Contain("writer.writeDateTimeOffset(value.createdAt)");
        generatedCode.Should().Contain("reader.readDateTimeOffset()");
        generatedCode.Should().Contain("writer.writeNullableDateTimeOffset(value.updatedAt)");
        generatedCode.Should().Contain("reader.readNullableDateTimeOffset()");
    }

    [Fact]
    public void GenerateTypeScriptReadOnlyCollections()
    {
        // IReadOnlyList<T>/IReadOnlyCollection<T>/IReadOnlyDictionary<K,V> used to throw MEMPACK031.
        var generatedCode = CompileAndRead(
            """
            using System;
            using System.Collections.Generic;
            using MemoryPack;

            [MemoryPackable]
            [GenerateTypeScript]
            public partial class Item
            {
                public int Id { get; set; }
            }

            [MemoryPackable]
            [GenerateTypeScript]
            public partial class Hoge
            {
                public IReadOnlyList<string> Names { get; set; }
                public IReadOnlyList<Item> Items { get; set; }
                public IReadOnlyCollection<double> Values { get; set; }
                public IReadOnlyDictionary<string, int> Counts { get; set; }
            }
            """,
            "Hoge.ts");

        generatedCode.Should().Contain("names: string[] | null;");
        generatedCode.Should().Contain("items: (Item | null)[] | null;");
        generatedCode.Should().Contain("values: number[] | null;");
        generatedCode.Should().Contain("counts: Map<string, number> | null;");
        generatedCode.Should().Contain("writer.writeArray(value.names, (writer, x) => writer.writeString(x))");
        generatedCode.Should().Contain("writer.writeArray(value.items, (writer, x) => Item.serializeCore(writer, x))");
        generatedCode.Should().Contain("reader.readArray(reader => reader.readFloat64())");
        generatedCode.Should().Contain("writer.writeMap(value.counts,");
        generatedCode.Should().Contain("reader.readMap(reader => reader.readString(), reader => reader.readInt32())");
    }

    const string DictionaryAsRecordSource = """
        using System;
        using System.Collections.Generic;
        using MemoryPack;

        [MemoryPackable]
        [GenerateTypeScript]
        public partial class Item
        {
            public int Id { get; set; }
        }

        public enum Color { Red, Blue }

        [MemoryPackable]
        [GenerateTypeScript]
        public partial class RecordHoge
        {
            public Dictionary<string, string> Tags { get; set; }
            public IReadOnlyDictionary<string, string> Attributes { get; set; }
            public Dictionary<int, double> Scores { get; set; }
            public Dictionary<Guid, Item> Items { get; set; }
            public IReadOnlyDictionary<string, int?> Counts { get; set; }
            public Dictionary<Guid, string> NamesById { get; set; }
            public Dictionary<long, int> ByLong { get; set; }
            public Dictionary<Color, int> ByColor { get; set; }
        }
        """;

    [Fact]
    public void GenerateTypeScriptDictionaryAsRecordIsOffByDefault()
    {
        var generatedCode = CompileAndRead(DictionaryAsRecordSource, "RecordHoge.ts", enableNullableTypes: false);

        generatedCode.Should().Contain("tags: Map<string | null, string | null> | null;");
        generatedCode.Should().Contain("reader.readMap(reader => reader.readString(), reader => reader.readString())");
        generatedCode.Should().NotContain("Record<");
        generatedCode.Should().NotContain("readRecord");
    }

    [Fact]
    public void GenerateTypeScriptDictionaryAsRecord()
    {
        var generatedCode = CompileAndRead(DictionaryAsRecordSource, "RecordHoge.ts", enableNullableTypes: false, dictionaryAsRecord: true);

        // string -> string: dedicated closure-free helpers; null key/value coalesce to "" so the value type is plain string
        generatedCode.Should().Contain("tags: Record<string, string> | null;");
        generatedCode.Should().Contain("attributes: Record<string, string> | null;");
        generatedCode.Should().Contain("value.tags = reader.readStringRecord();");
        generatedCode.Should().Contain("value.attributes = reader.readStringRecord();");
        generatedCode.Should().Contain("writer.writeStringRecord(value.tags);");

        // other string-valued records use the generic helper with the same "" coalescing
        generatedCode.Should().Contain("namesById: Record<string, string> | null;");
        generatedCode.Should().Contain("value.namesById = reader.readRecord(reader => reader.readGuid(), reader => reader.readString() ?? \"\");");
        generatedCode.Should().Contain("writer.writeRecord(value.namesById, (writer, x) => writer.writeGuid(x), (writer, x) => writer.writeString(x));");

        // number keys: Object.keys yields strings, converted back on write
        generatedCode.Should().Contain("scores: Record<number, number> | null;");
        generatedCode.Should().Contain("reader.readRecord(reader => reader.readInt32(), reader => reader.readFloat64())");
        generatedCode.Should().Contain("writer.writeRecord(value.scores, (writer, x) => writer.writeInt32(Number(x)), (writer, x) => writer.writeFloat64(x));");

        // Guid keys are strings; non-string values keep their own type
        generatedCode.Should().Contain("items: Record<string, Item | null> | null;");
        generatedCode.Should().Contain("reader.readRecord(reader => reader.readGuid(), reader => Item.deserializeCore(reader))");
        generatedCode.Should().Contain("counts: Record<string, number | null> | null;");
        generatedCode.Should().Contain("reader.readRecord(reader => reader.readString() ?? \"\", reader => reader.readNullableInt32())");

        // bigint and enum keys fall back to Map
        generatedCode.Should().Contain("byLong: Map<bigint, number> | null;");
        generatedCode.Should().Contain("byColor: Map<Color, number> | null;");
    }

    [Fact]
    public void GenerateTypeScriptDictionaryAsRecordKeepsDeclaredNullableStringValues()
    {
        var generatedCode = CompileAndRead(
            """
            #nullable enable
            using System.Collections.Generic;
            using MemoryPack;

            [MemoryPackable]
            [GenerateTypeScript]
            public partial class NullableValues
            {
                public Dictionary<string, string?>? Maybe { get; set; }
                public Dictionary<string, string>? Always { get; set; }
            }
            """,
            "NullableValues.ts",
            enableNullableTypes: true,
            dictionaryAsRecord: true);

        generatedCode.Should().Contain("maybe: Record<string, string | null> | null;");
        generatedCode.Should().Contain("reader.readRecord(reader => reader.readString() ?? \"\", reader => reader.readString())");
        generatedCode.Should().Contain("always: Record<string, string> | null;");
        generatedCode.Should().Contain("value.always = reader.readStringRecord();");
    }

    [Fact]
    public void GenerateTypeScriptRuntimeHasRecordHelpers()
    {
        var reader = CompileAndRead(DictionaryAsRecordSource, "MemoryPackReader.ts", dictionaryAsRecord: true);
        var writer = File.ReadAllText(Path.Combine(Path.GetTempPath(), "MemoryPackWriter.ts"));

        reader.Should().Contain("public readRecord<K extends string | number, V>(");
        reader.Should().Contain("public readStringRecord(): Record<string, string> | null");
        reader.Should().Contain("if (key === \"__proto__\")");
        writer.Should().Contain("public writeRecord<V>(");
        writer.Should().Contain("public writeStringRecord(value: Record<string, string> | null): void");
    }

    [Fact]
    public void GenerateTypeScriptRuntimeNullableReadersInspectOnlyHasValueByte()
    {
        // Nullable<T> is blitted as HasValue(1 byte) + padding; the padding of a null value is not
        // guaranteed to be zero, so reading the whole flag width as an integer can turn null into a value.
        var runtime = CompileAndRead(
            """
            using MemoryPack;

            [MemoryPackable]
            [GenerateTypeScript]
            public partial class Hoge
            {
                public long? Value { get; set; }
            }
            """,
            "MemoryPackReader.ts");

        runtime.Should().Contain("private readHasValue(size: number): boolean");
        runtime.Should().Contain("if (!this.readHasValue(8))");
        runtime.Should().NotContain("if (this.readInt64() == 0n)");
        runtime.Should().NotContain("if (this.readUint64() == 0n)");
    }


    class TypeScriptOptionProvider : AnalyzerConfigOptionsProvider
    {
        readonly Dictionary<string, string> _values = new();

        public string this[string key]
        {
            get => _values[key];
            set => _values[key] = value;
        }

        public override AnalyzerConfigOptions GlobalOptions => new SimpleOptions(_values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return new SimpleOptions(_values);
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return new SimpleOptions(_values);
        }

        public class SimpleOptions : AnalyzerConfigOptions
        {
            readonly Dictionary<string, string> _configValues;

            public SimpleOptions(Dictionary<string, string> configValues)
            {
                _configValues = configValues;
            }

            /// <inheritdoc />
            public override IEnumerable<string> Keys => _configValues.Keys;

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (_configValues.ContainsKey(key))
                {
                    value = _configValues[key];

                    return true;
                }

                if (key == "build_property.MemoryPackGenerator_TypeScriptOutputDirectory")
                {
                    value = Path.GetTempPath();
                    return true;
                }

                value = null;
                return false;
            }
        }
    }
}

[MemoryPackable]
[GenerateTypeScript]
public partial class Hoge
{
    public LongEnum? MyProperty { get; set; }
}

public enum LongEnum : long
{

}

public enum ULongEnum : ulong
{

}

#endif
