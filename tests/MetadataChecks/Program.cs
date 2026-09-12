using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

if (args.Length != 4) throw new ArgumentException("MetadataChecks <mod.dll> <original Managed> <BepInEx core> <report.json>");
var resolver = new DefaultAssemblyResolver();
foreach (string dir in resolver.GetSearchDirectories()) resolver.RemoveSearchDirectory(dir);
resolver.AddSearchDirectory(args[1]); resolver.AddSearchDirectory(args[2]);
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
using var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });
var failures = new List<string>();
int calls = 0, patches = 0, injected = 0, accessors = 0;
void Require(bool condition, string error) { if (!condition) failures.Add(error); }
IEnumerable<TypeDefinition> All(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(All));
string Identity(TypeReference t)
{
    if (t is ByReferenceType b) return Identity(b.ElementType);
    if (t is GenericInstanceType g) return Identity(g.ElementType) + "<" + string.Join(",", g.GenericArguments.Select(Identity)) + ">";
    if (t is ArrayType a) return Identity(a.ElementType) + "[]";
    var resolved = t.Resolve();
    return resolved == null ? t.FullName : resolved.Module.Assembly.Name.Name + ":" + resolved.FullName;
}
FieldDefinition? FindField(TypeDefinition t, string name)
{
    for (TypeDefinition? current = t; current != null; current = current.BaseType?.Resolve())
    {
        var field = current.Fields.FirstOrDefault(f => f.Name == name);
        if (field != null) return field;
    }
    return null;
}
Require(!module.AssemblyReferences.Any(r => r.Name == "Jotunn"), "Jotunn assembly reference remains");
foreach (var type in module.Types.SelectMany(All))
foreach (var method in type.Methods)
{
    if (method.HasBody)
    foreach (var ins in method.Body.Instructions)
    {
        if (ins.Operand is FieldReference f && f.DeclaringType.FullName == "ZRoutedRpc" && f.Name == "Everybody")
            Require(ins.OpCode.Code != Code.Ldsfld, method.FullName + ": old Everybody ldsfld");
        if (!type.FullName.StartsWith("HarnessPrefabs.")) continue;
        if (ins.Operand is MemberReference member && member is MethodReference or FieldReference &&
            member.DeclaringType.Scope.Name is ("assembly_valheim" or "assembly_utils" or "assembly_guiutils" or "SoftReferenceableAssets"))
        {
            try
            {
                var definition = member is MethodReference mr ? (IMemberDefinition?)mr.Resolve() : ((FieldReference)member).Resolve();
                Require(definition != null, "Unresolved " + member.FullName);
                if (definition is MethodDefinition md) Require(md.IsPublic, "Direct nonpublic method " + method.FullName + " -> " + member.FullName);
                if (definition is FieldDefinition fd) Require(fd.IsPublic, "Direct nonpublic field " + method.FullName + " -> " + member.FullName);
                calls++;
            }
            catch (Exception ex) { failures.Add("Resolve " + member.FullName + ": " + ex.Message); }
        }
        if (ins.Operand is GenericInstanceMethod accessor && accessor.Name == "FieldRefAccess" && accessor.GenericArguments.Count == 2)
        {
            var previous = ins.Previous;
            if (previous?.OpCode.Code != Code.Ldstr) { failures.Add("Unreviewed computed FieldRef target " + method.FullName); continue; }
            var owner = accessor.GenericArguments[0].Resolve();
            var field = FindField(owner, (string)previous.Operand);
            Require(field != null && Identity(field.FieldType) == Identity(accessor.GenericArguments[1]), "FieldRef mismatch " + accessor.FullName + " " + previous.Operand);
            accessors++;
        }
    }
    if (!type.FullName.StartsWith("HarnessPrefabs.") || !(method.Name is "Prefix" or "Postfix" || method.CustomAttributes.Any(a => a.AttributeType.FullName is "HarmonyLib.HarmonyPrefix" or "HarmonyLib.HarmonyPostfix"))) continue;
    var attrs = type.CustomAttributes.Concat(method.CustomAttributes).Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToList();
    var values = attrs.SelectMany(a => a.ConstructorArguments).ToList();
    var targetType = values.Select(v => v.Value).OfType<TypeReference>().FirstOrDefault()?.Resolve();
    var targetName = values.Select(v => v.Value).OfType<string>().FirstOrDefault();
    if (targetType == null || targetName == null) { failures.Add("Unresolved patch declaration " + method.FullName); continue; }
    var targets = targetType.Methods.Where(m => m.Name == targetName).ToList();
    Require(targets.Count == 1, "Ambiguous/missing target " + targetType.FullName + "." + targetName);
    if (targets.Count != 1) continue;
    var target = targets[0]; patches++;
    foreach (var parameter in method.Parameters)
    {
        if (parameter.Name == "__state") continue; // Paired state remains in the same patch class.
        TypeReference? expected = parameter.Name switch
        {
            "__instance" => target.DeclaringType,
            "__result" => target.ReturnType,
            _ when parameter.Name.StartsWith("___") => FindField(target.DeclaringType, parameter.Name[3..])?.FieldType,
            _ => target.Parameters.FirstOrDefault(p => p.Name == parameter.Name)?.ParameterType
        };
        Require(expected != null && Identity(expected) == Identity(parameter.ParameterType), "Injection mismatch " + method.FullName + ":" + parameter.Name);
        injected++;
    }
}
var report = new { candidate = Path.GetFullPath(args[0]), originalManaged = Path.GetFullPath(args[1]), calls, patches, injected, accessors, failures,
    limits = "Metadata checks with resolved type-forwarder identities. Game calls and explicit FieldRefs/Harmony injections checked; no Unity lifecycle/patch application/network execution. ServerSync behavior is covered separately." };
File.WriteAllText(args[3], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{calls} game calls, {patches} patches, {injected} injections, {accessors} FieldRefs; {failures.Count} failures");
foreach (var failure in failures) Console.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;
