using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.IO.Compression;
namespace Workbench;
public static class MarkdownTests {
 public static void Run(Workspace w,List<string> logs) {
  var p=w.Create("Markdown夹具","MD初始目标");
  Tests.Check(p.Format=="markdown-v3"&&w.CheckTemplate(p).Length==0,"real project directory and thin workflow template",logs);
  Tests.Check(!File.Exists(w.Safe("data/local/Projects/"+p.Id+"/repo/docs/records.json")),"new project has no JSON truth copy",logs);
  var firstPointer=w.RecordPath(p.Id);var heldFirst=w.Safe("data/local/Projects/"+p.Id+"/runs/held-first-pointer.md");
  File.Move(firstPointer,heldFirst);var firstRecovered=w.Restore(p.Id);
  Tests.Check(firstRecovered.Version==1&&firstRecovered.Goal=="MD初始目标","complete prepared first snapshot explicitly recoverable",logs);
  var t=new WorkTask{Goal="任务正文由AI直接读取",Scope="module-a",Next="按同一任务卡交接"};
  p.Tasks.Add(t);p.Knowledge.Add(new Fact{Id="K-a",Text="模块A批准要求",Scope="module-a",Role="已批准意图",Source="fixture approval"});
  p.Knowledge.Add(new Fact{Id="K-b",Text="模块B无关要求",Scope="module-b",Role="已批准意图",Source="fixture B"});
  w.Save(p,p.Version,"Markdown task fixture");
  Tests.Check(File.ReadAllText(w.SnapshotFile(p.Id,"tasks/"+t.Id+".md")).Contains(t.Goal),"AI directly reads canonical task Markdown",logs);
  var loaded=w.Load(p.Id);var original=w.SnapshotFile(p.Id,"PROJECT.md");var oldText=File.ReadAllText(original);
  File.WriteAllText(original,oldText.Replace("MD初始目标","外部Markdown新目标"));
  var external=w.Load(p.Id);
  Tests.Check(w.Verify(loaded,loaded.Tasks[0]).StartsWith("needs_verification"),"open window cannot verify stale Markdown model",logs);
  try{w.Package(loaded,loaded.Tasks[0]);throw new Exception("mixed old content new hashes");}catch(InvalidOperationException){logs.Add("PASS: stale window package generation rejected");}
  try{w.RecordEvidence(loaded,loaded.Tasks[0],"runs/check.txt");throw new Exception("stale evidence recorded");}catch(InvalidOperationException){logs.Add("PASS: stale window evidence recording rejected");}
  Tests.Check(external.Goal=="外部Markdown新目标"&&external.ExternalChanges.Contains("PROJECT.md"),"external Markdown edits safely re-read",logs);
  try{w.Save(loaded,loaded.Version,"stale overwrite");throw new Exception("external source overwritten");}catch(InvalidOperationException){logs.Add("PASS: old App state cannot overwrite external Markdown");}
  try{w.Save(external,external.Version,"unapproved sync");throw new Exception("unapproved synchronized");}catch(InvalidOperationException){logs.Add("PASS: unconfirmed external intent not auto-approved");}
  Tests.Check(File.ReadAllText(original).Contains("外部Markdown新目标")&&w.ExternalDiff(external).Contains("MD初始目标"),"diff preserves original baseline and new body",logs);
  w.AcceptExternal(external);p=w.Load(p.Id);t=p.Tasks[0];
  Tests.Check(p.ExternalChanges.Count==0&&p.Goal=="外部Markdown新目标","explicit review commits same Markdown facts",logs);
  var canonical=w.SnapshotFile(p.Id,"PROJECT.md");var beforeNote=File.ReadAllText(canonical);
  File.AppendAllText(canonical,"\n## 未登记正文\n必须保留，不能静默丢弃。");
  try{w.Load(p.Id);throw new Exception("extra source silently ignored");}catch(InvalidDataException){logs.Add("PASS: unrecognized source content blocked without overwrite");}
  Tests.Check(File.ReadAllText(canonical).Contains("必须保留"),"unrecognized prose preserved on disk",logs);
  File.WriteAllText(canonical,beforeNote);
  var source=w.Safe("data/local/Projects/"+p.Id+"/repo/src/untracked.cs");Directory.CreateDirectory(Path.GetDirectoryName(source)!);File.WriteAllText(source,"fixture source v1");
  t.RelatedFiles.Add("repo/src/untracked.cs");w.Save(p,p.Version,"register exact input");
  var evidence=w.Safe("data/local/Projects/"+p.Id+"/runs/check.txt");File.WriteAllText(evidence,"fixture result only, not actual AI integration");
  w.RecordEvidence(p,t,"runs/check.txt");w.Save(p,p.Version,"record actual fixture file");
  Tests.Check(w.Verify(p,t).StartsWith("verified"),"source registration plus evidence anchor valid",logs);
  var package=w.Package(p,t);Tests.Check(package.Contains("Markdown来源版本")&&package.Contains("SHA256")&&!package.Contains("模块B无关要求"),"context exact versions relevant sources only",logs);
  Tests.Check(w.Package(p,t)==package,"repeat context generation deterministic",logs);
  File.WriteAllText(source,"fixture dirty source v2");
  Tests.Check(w.Verify(p,t).StartsWith("needs_verification"),"registered untracked dirty input invalidates evidence",logs);
  try{w.Export(p,t,package,w.Anchor(p,t));throw new Exception("dirty exported");}catch(InvalidOperationException){logs.Add("PASS: dirty input makes old context unusable");}
  File.WriteAllText(source,"fixture source v1");
  File.WriteAllText(w.Safe("data/local/Projects/"+p.Id+"/assets/unrelated.txt"),"unrelated");
  Tests.Check(w.Verify(p,t).StartsWith("verified"),"unrelated unregistered file does not invalidate task evidence",logs);
  p.Knowledge[1].Text="模块B修改";w.Save(p,p.Version,"unrelated module edit");
  Tests.Check(w.Verify(p,t).StartsWith("verified"),"unrelated module does not invalidate task evidence",logs);
  File.Move(source,w.Safe("data/local/Projects/"+p.Id+"/runs/held-source.cs"));
  Tests.Check(w.Verify(p,t).StartsWith("unknown")&&w.CheckTemplate(p).Any(x=>x.Contains("来源缺失")),"missing registered input is unknown",logs);
  try{w.RecordEvidence(p,t,"runs/check.txt");throw new Exception("missing input verified");}catch(InvalidOperationException){logs.Add("PASS: missing input cannot become verified");}
  File.Move(w.Safe("data/local/Projects/"+p.Id+"/runs/held-source.cs"),source);
  try{w.NormalizeRelated("../P-other/repo/input.cs");throw new Exception("cross-project input allowed");}catch(InvalidOperationException){logs.Add("PASS: cross-project source reference rejected");}
  foreach(var unsafePath in new[]{"REPO/DOCS/SNAPSHOTS/source.md","repo/docs/current.md","repo/src/con.txt","repo/src/file.","repo/src/dir /file.txt"}){
   try{w.NormalizeRelated(unsafePath);throw new Exception("unsafe alias accepted");}catch(InvalidOperationException){logs.Add("PASS: Windows unsafe source alias rejected "+unsafePath);}
  }
  Tests.Check(w.FactStorageBytes(p.Id)>new FileInfo(w.RecordPath(p.Id)).Length,"storage includes actual registered docs and history",logs);
  var invalid=Workspace.Clone(p);invalid.Tasks[0].RelatedFiles.Add("../../other/file");
  try{w.Save(invalid,invalid.Version,"invalid input");throw new Exception("invalid source committed");}catch(InvalidOperationException){logs.Add("PASS: illegal source rejected before commit");}
  var fact=w.SnapshotFile(p.Id,"knowledge/K-a.md");var factText=File.ReadAllText(fact);
  File.WriteAllText(fact,factText.Replace(p.Id,"P-000000000000"));
  try{w.Load(p.Id);throw new Exception("foreign fact loaded");}catch(InvalidDataException){logs.Add("PASS: Markdown foreign project identity blocked");}
  File.WriteAllText(fact,factText);
  var folder=w.Safe("data/local/Projects/"+p.Id+"/repo/docs/snapshots/S-999-abcdef123456");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"PROJECT.md"),"partial uncommitted fixture");
  Tests.Check(w.Load(p.Id).Version==p.Version,"interrupted uncommitted multi-file snapshot not active",logs);
  var workflow=w.Safe("data/local/Projects/"+p.Id+"/repo/docs/WORKFLOW.md");var held=w.Safe("data/local/Projects/"+p.Id+"/runs/held-workflow.md");
  File.Move(workflow,held);var missing=w.Load(p.Id);Tests.Check(w.CheckTemplate(missing).Any(x=>x=="缺少 repo/docs/WORKFLOW.md"),"partial template reported incomplete",logs);
  w.FillMissingTemplate(missing);p=w.Load(p.Id);Tests.Check(w.CheckTemplate(p).Length==0,"explicit fill only missing template",logs);
  File.AppendAllText(workflow,"\n用户自定义规范，不可覆盖。");p=w.Load(p.Id);w.FillMissingTemplate(p);
  Tests.Check(File.ReadAllText(workflow).Contains("用户自定义规范"),"template repair preserves existing customized rule",logs);
  w.AcceptExternal(p);p=w.Load(p.Id);
  var backup=w.Backup(p);using var archive=ZipFile.OpenRead(backup);
  Tests.Check(archive.GetEntry("repo/docs/CURRENT.md")!=null&&archive.Entries.Any(e=>e.FullName.Contains("/tasks/")&&e.FullName.EndsWith(".md"))&&archive.Entries.Any(e=>e.FullName.StartsWith("archive/document-baselines/")),"backup contains canonical Markdown and review baselines",logs);
  var activePath=w.RecordPath(p.Id);var activePointer=File.ReadAllText(activePath);
  File.WriteAllText(activePath,"broken pointer");var restored=w.Restore(p.Id);
  Tests.Check(restored.Id==p.Id&&restored.Format=="markdown-v3","atomic pointer restore retains Markdown facts",logs);
  File.WriteAllText(activePath,activePointer);
  var legacyId="P-"+Guid.NewGuid().ToString("N")[..12];var legacyRoot="data/local/Projects/"+legacyId;
  foreach(var relative in new[]{"repo/docs","docs","assets","data","runs","releases","archive"})Directory.CreateDirectory(w.Safe(legacyRoot+"/"+relative));
  var legacyRecord=new ProjectRecord{Id=legacyId,Name="旧版测试夹具",Goal="保留既有批准目标",Version=2};
  var legacyFile=w.Safe(legacyRoot+"/repo/docs/records.json");File.WriteAllText(legacyFile,System.Text.Json.JsonSerializer.Serialize(legacyRecord));
  File.WriteAllText(w.Safe(legacyRoot+"/README.md"),"事实源：[repo/docs/records.json](repo/docs/records.json)。\n用户附加文字应保留。");
  var legacy=w.Load(legacyId);Tests.Check(legacy.Format=="legacy-json","legacy source not silently migrated",logs);
  var legacyBytes=File.ReadAllBytes(legacyFile);w.UpgradeMarkdown(legacy);
  Tests.Check(w.Load(legacyId).Format=="markdown-v3"&&File.ReadAllBytes(legacyFile).SequenceEqual(legacyBytes)&&File.ReadAllText(w.Safe(legacyRoot+"/README.md")).Contains("CURRENT.md"),"explicit upgrade preserves historical JSON and links Markdown",logs);
  var newLoad=w.Load(p.Id);var activeProject=w.SnapshotFile(p.Id,"PROJECT.md");var heldDoc=w.Safe("data/local/Projects/"+p.Id+"/runs/held-project.md");
  File.Move(activeProject,heldDoc);
  try{w.Load(p.Id);throw new Exception("missing current doc silently replaced");}catch(FileNotFoundException){logs.Add("PASS: missing active document has no JSON fallback");}
  File.Move(heldDoc,activeProject);
 }
}
