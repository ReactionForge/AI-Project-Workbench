using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;

namespace Workbench;
public static class GitAssetTests {
 static void Git(Workspace w,string repo,params string[] args){
  var result=Workspace.GitProcess(repo,w.Root,args);if(result.Code!=0)throw new IOException("fixture Git command failed: "+args[0]);
 }
 public static ProjectRecord Fixture(Workspace w){
  var p=w.Create("Git与资产（测试夹具）","仅验证本项目受控来源与恢复");var t=new WorkTask{Goal="核对代码与完整内容",Scope="repo/src",RelatedFiles=new(){"repo/src/engine.cs"}};
  p.Tasks.Add(t);w.Save(p,p.Version,"git and content fixture");
  var repo=w.Safe("data/"+w.Mode+"/Projects/"+p.Id+"/repo");Directory.CreateDirectory(Path.Combine(repo,"src"));
  File.WriteAllText(Path.Combine(repo,"src/engine.cs"),"fixture v1");
  File.WriteAllText(Path.Combine(repo,"src/中文 名称.cs"),"fixture Unicode path");
  Git(w,repo,"init","--initial-branch=fixture");
  Git(w,repo,"add","--","src/engine.cs","src/中文 名称.cs");
  Git(w,repo,"-c","user.name=Workbench Fixture","-c","user.email=fixture@example.invalid","-c","commit.gpgsign=false","commit","-m","isolated fixture only");
  return p;
 }
 static void BadZip(string path,string[] names,bool symlink=false){
  using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
  foreach(var name in names){var entry=zip.CreateEntry(name);if(symlink)entry.ExternalAttributes=unchecked((int)0xA1FF0000);using var writer=new StreamWriter(entry.Open());writer.Write("fixture malicious content");}
 }
 static void Repacked(string original,string target,ContentBackup m,string changedPath,string text){
  m=JsonSerializer.Deserialize<ContentBackup>(JsonSerializer.Serialize(m))!;
  byte[] changed=Encoding.UTF8.GetBytes(text);var changedEntry=m.Files.First(f=>f.Path==changedPath);changedEntry.Bytes=changed.LongLength;changedEntry.Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(changed));
  m.ContentStamp=Workspace.ContentFingerprint(m);
  using var source=ZipFile.OpenRead(original);using var output=ZipFile.Open(target,ZipArchiveMode.Create);
  foreach(var f in m.Files){var entry=output.CreateEntry("project/"+f.Path);using var stream=entry.Open();if(f.Path==changedPath)stream.Write(changed);else{using var input=source.GetEntry("project/"+f.Path)!.Open();input.CopyTo(stream);}}
  var manifest=output.CreateEntry("workbench-backup.json");using var writer=new StreamWriter(manifest.Open());writer.Write(JsonSerializer.Serialize(m));
 }
 public static void Run(Workspace w,List<string> logs){
  var p=Fixture(w);var t=p.Tasks[0];string prefix="data/local/Projects/"+p.Id+"/";
  var repo=w.Safe(prefix+"repo");var source=Path.Combine(repo,"src/engine.cs");var index=Path.Combine(repo,".git/index");
  var indexBefore=File.ReadAllBytes(index);var headBefore=File.ReadAllBytes(Path.Combine(repo,".git/HEAD"));
  var clean=w.ReadGit(p,t);
  Tests.Check(clean.State=="observed"&&clean.Branch=="fixture"&&clean.Commit.Length==40&&clean.Files.Count==2&&clean.Files.All(f=>f.State=="clean"),"real fixture Git commit branch and clean scope",logs);
  Tests.Check(clean.Files.Any(f=>f.Path=="src/中文 名称.cs"),"Git NUL parser preserves Unicode spaces",logs);
  Tests.Check(File.ReadAllBytes(index).SequenceEqual(indexBefore)&&File.ReadAllBytes(Path.Combine(repo,".git/HEAD")).SequenceEqual(headBefore),"Git observation does not update index or HEAD",logs);
  File.WriteAllText(w.Safe(prefix+"runs/result.txt"),"fixture evidence; actual isolated Git and file IO only");
  w.RecordEvidence(p,t,"runs/result.txt");w.Save(p,p.Version,"record fixture evidence");
  Tests.Check(t.LastVerifiedCodeCommit==clean.Commit&&w.Verify(p,t).StartsWith("verified"),"evidence retains actual verified code commit",logs);
  var package=w.Package(p,t);var anchor=w.Anchor(p,t);
  Tests.Check(package.Contains(clean.Commit)&&package.Contains("分支：fixture")&&!package.Contains("Git 提交/脏文件：未接入"),"task package reports actual scoped Git facts",logs);
  Tests.Check(w.Package(p,t)==package,"Git package repeated generation stable",logs);
  File.WriteAllText(source,"fixture dirty v2");
  Tests.Check(w.ReadGit(p,t).Files.Any(f=>f.Path=="src/engine.cs"&&f.State==" M")&&w.Verify(p,t).StartsWith("needs_verification"),"working dirty file invalidates evidence",logs);
  try{w.Export(p,t,package,anchor);throw new Exception("dirty package exported");}catch(InvalidOperationException){logs.Add("PASS: Git dirty source expires prior task package");}
  Git(w,repo,"add","--","src/engine.cs");
  var staged=w.ReadGit(p,t);Tests.Check(staged.Files.Any(f=>f.State=="M "&&f.Index!=clean.Files.First(c=>c.Path==f.Path).Index),"staged index change detected",logs);
  File.WriteAllText(source,"fixture v1");
  Tests.Check(w.Anchor(p,t)!=anchor,"staged-only source mismatch changes anchor",logs);
  Git(w,repo,"add","--","src/engine.cs");
  File.WriteAllText(Path.Combine(repo,"src/new.txt"),"untracked one");
  var untracked=w.ReadGit(p,t);Tests.Check(untracked.Files.Any(f=>f.Path=="src/new.txt"&&f.State=="??"),"untracked affected file captured",logs);
  var firstUntracked=untracked.Fingerprint();File.WriteAllText(Path.Combine(repo,"src/new.txt"),"untracked two");
  Tests.Check(w.ReadGit(p,t).Fingerprint()!=firstUntracked,"untracked content change detected",logs);
  Directory.CreateDirectory(Path.Combine(repo,"other"));File.WriteAllText(Path.Combine(repo,"other/irrelevant.txt"),"irrelevant");
  Tests.Check(!w.ReadGit(p,t).Files.Any(f=>f.Path.Contains("irrelevant")),"Git scope excludes unrelated module",logs);
  Git(w,repo,"add","--","src/new.txt");Git(w,repo,"-c","user.name=Workbench Fixture","-c","user.email=fixture@example.invalid","-c","commit.gpgsign=false","commit","-m","fixture second version");
  Tests.Check(w.ReadGit(p,t).Commit!=clean.Commit&&w.Verify(p,t).StartsWith("needs_verification"),"new commit requires task recheck",logs);
  Git(w,repo,"branch","fixture-copy");Git(w,repo,"symbolic-ref","HEAD","refs/heads/fixture-copy");
  Tests.Check(w.ReadGit(p,t).Branch=="fixture-copy","actual branch change recorded",logs);
  var config=Path.Combine(repo,".git/config");var configBefore=File.ReadAllText(config);File.AppendAllText(config,"\n[include]\npath = ignored-fixture-config\n");
  Tests.Check(w.ReadGit(p,t).State=="unavailable"&&w.Verify(p,t).StartsWith("unknown"),"Git config include blocked before invocation",logs);
  File.WriteAllText(config,configBefore+"\n[filter \"fixture\"]\nclean = do-not-run-fixture-placeholder\n");
  Tests.Check(w.ReadGit(p,t).State=="unavailable","Git clean filter blocked before external command",logs);
  File.WriteAllText(config,configBefore);
  var noGit=w.Create("未配置Git夹具","不能搜索父仓库");var noTask=new WorkTask{Goal="unknown git"};
  Tests.Check(w.ReadGit(noGit,noTask).State=="not-configured","missing own .git does not discover parent repository",logs);
  var unborn=w.Create("Git未提交夹具","新仓库没有代码提交");var unbornRepo=w.Safe("data/local/Projects/"+unborn.Id+"/repo");Git(w,unbornRepo,"init","--initial-branch=unborn-fixture");
  Tests.Check(w.ReadGit(unborn,noTask).State=="unborn"&&w.ReadGit(unborn,noTask).Commit=="unborn","unborn Git never invents a verified commit",logs);
  File.WriteAllText(w.Safe("data/local/Projects/"+noGit.Id+"/repo/.git"),"gitdir: linked-fixture-target");
  Tests.Check(w.ReadGit(noGit,noTask).State=="unsupported","linked Git metadata rejected without following",logs);
  // Full content fixtures: deliberately fake placeholders, never real credentials.
  File.WriteAllBytes(w.Safe(prefix+"assets/sample.bin"),new byte[]{0,1,2,255});
  File.WriteAllText(w.Safe(prefix+"data/settings.json"),"{\"fixture\":true}");
  File.WriteAllText(w.Safe(prefix+"docs/reference.md"),"fixture input reference");
  File.WriteAllText(w.Safe(prefix+"assets/.env"),"fake fixture placeholder");
  File.WriteAllText(w.Safe(prefix+"assets/sensitive.txt"),"api_key=DEMO_ONLY_123456789");
  Directory.CreateDirectory(w.Safe(prefix+"repo/obj"));File.WriteAllText(w.Safe(prefix+"repo/obj/cache.txt"),"fixture rebuild cache");
  Directory.CreateDirectory(w.Safe(prefix+"assets/empty"));
  var plan=w.PreviewFullBackup(p);
  Tests.Check(plan.Files.Any(f=>f.Path=="assets/sample.bin")&&plan.Files.Any(f=>f.Path=="data/settings.json")&&plan.Files.Any(f=>f.Path=="docs/reference.md")&&plan.Files.Any(f=>f.Path=="runs/result.txt")&&plan.Files.Any(f=>f.Path.EndsWith("/PROJECT.md")),"content preview covers assets data inputs evidence and canonical Markdown",logs);
  Tests.Check(plan.Excluded.Any(f=>f.Path=="repo/.git")&&plan.Excluded.Any(f=>f.Path=="assets/.env")&&plan.Excluded.Any(f=>f.Path=="assets/sensitive.txt")&&plan.Excluded.Any(f=>f.Path=="repo/obj"),"backup excludes Git metadata credentials and rebuild cache",logs);
  Tests.Check(plan.GitAtBackup[t.Id].Commit==w.ReadGit(p,t).Commit&&plan.GitAtBackup[t.Id].Branch=="fixture-copy","backup retains observed Git source metadata without claiming Git history",logs);
  File.AppendAllText(w.Safe(prefix+"data/settings.json")," ");
  try{w.CreateFullBackup(p,plan);throw new Exception("stale backup created");}catch(InvalidOperationException){logs.Add("PASS: asset change expires backup preview");}
  plan=w.PreviewFullBackup(p);var zip=w.CreateFullBackup(p,plan);var repeated=w.CreateFullBackup(p,plan);
  Tests.Check(zip!=repeated&&File.Exists(zip)&&File.Exists(repeated),"repeated content backup never overwrites",logs);
  var recovery=w.PreviewContentRestore(zip);var originalAsset=File.ReadAllBytes(w.Safe(prefix+"assets/sample.bin"));
  Tests.Check(!Directory.Exists(recovery.Destination)&&recovery.Content.ContentStamp==plan.ContentStamp,"restore preview validates content without creating destination",logs);
  try{w.RestoreContentCopy(recovery,false);throw new Exception("unconfirmed restore");}catch(InvalidOperationException){logs.Add("PASS: restore requires explicit confirmation");}
  string restoredRoot=w.RestoreContentCopy(recovery,true);var restored=new Workspace(restoredRoot);var restoredRecord=restored.Load(p.Id);
  Tests.Check(restoredRecord.SourceStamp==p.SourceStamp&&restoredRecord.Goal==p.Goal,"restore copy retains canonical identity version and approved goal",logs);
  Tests.Check(File.ReadAllBytes(restored.Safe(prefix+"assets/sample.bin")).SequenceEqual(originalAsset)&&File.Exists(restored.Safe(prefix+"data/settings.json"))&&Directory.Exists(restored.Safe(prefix+"assets/empty")),"restore copy retains binary assets data and empty directories",logs);
  var reopened=RecoveryStartup.Resolve(w.Root,restoredRoot);
  Tests.Check(reopened.Root==restoredRoot&&reopened.Load(p.Id).SourceStamp==p.SourceStamp,"restored workspace can reopen without re-import or migration",logs);
  try{RecoveryStartup.Resolve(w.Root,repo);throw new Exception("arbitrary workspace opened");}catch(InvalidOperationException){logs.Add("PASS: recovery reopen rejects arbitrary project roots");}
  Tests.Check(File.ReadAllBytes(w.Safe(prefix+"assets/sample.bin")).SequenceEqual(originalAsset)&&File.Exists(zip),"restore preserves original project and source ZIP",logs);
  Tests.Check(!Directory.Exists(restored.Safe(prefix+"repo/.git"))&&!File.Exists(restored.Safe(prefix+"assets/.env"))&&restored.ReadGit(restoredRecord,restoredRecord.Tasks[0]).State=="not-configured","restored copy does not pretend excluded Git or secrets were restored",logs);
  Tests.Check(restored.Verify(restoredRecord,restoredRecord.Tasks[0]).StartsWith("needs_verification"),"restored no-Git copy requires original evidence recheck",logs);
  try{w.RestoreContentCopy(recovery,true);throw new Exception("target overwritten");}catch(IOException){logs.Add("PASS: repeated restore destination conflict preserves copy");}
  var testFolder=w.Safe("runs/content-adversarial");Directory.CreateDirectory(testFolder);
  foreach(var bad in new[]{"../escape","project/../escape","project/assets/file:stream","project/assets/CON.txt","project/assets/file.","/project/absolute"}){
   var path=Path.Combine(testFolder,Guid.NewGuid().ToString("N")+".zip");BadZip(path,new[]{bad});
   try{w.PreviewContentRestore(path);throw new Exception("unsafe ZIP previewed "+bad);}catch(InvalidDataException){logs.Add("PASS: unsafe ZIP path blocked "+bad);}
  }
  var duplicate=Path.Combine(testFolder,"duplicate.zip");BadZip(duplicate,new[]{"project/assets/a.txt","project/assets/A.txt"});
  try{w.PreviewContentRestore(duplicate);throw new Exception("ZIP case collision accepted");}catch(InvalidDataException){logs.Add("PASS: ZIP case duplicate blocked");}
  var symbolic=Path.Combine(testFolder,"symbolic.zip");BadZip(symbolic,new[]{"project/assets/link"},true);
  try{w.PreviewContentRestore(symbolic);throw new Exception("ZIP symlink accepted");}catch(InvalidDataException){logs.Add("PASS: ZIP symbolic link blocked");}
  var corrupt=Path.Combine(testFolder,"corrupt.zip");File.WriteAllText(corrupt,"not a zip");
  try{w.PreviewContentRestore(corrupt);throw new Exception("corrupt archive accepted");}catch(InvalidDataException){logs.Add("PASS: corrupt ZIP reports failure before restore");}
  var changing=Path.Combine(testFolder,"changing.zip");File.Copy(zip,changing);var staleRestore=w.PreviewContentRestore(changing);using(var append=new FileStream(changing,FileMode.Append))append.WriteByte(42);
  try{w.RestoreContentCopy(staleRestore,true);throw new Exception("changed archive restored");}catch(InvalidOperationException){logs.Add("PASS: changed ZIP expires restoration preview");}
  var currentDoc=Path.GetRelativePath(Path.GetDirectoryName(w.Safe(prefix+"README.md"))!,w.SnapshotFile(p.Id,"PROJECT.md")).Replace('\\','/');
  var projectDoc=plan.Files.First(f=>f.Path==currentDoc);
  var inconsistent=Path.Combine(testFolder,"inconsistent-facts.zip");
  Repacked(zip,inconsistent,plan,projectDoc.Path,File.ReadAllText(w.Safe(prefix+projectDoc.Path)).Replace(p.Goal,"Changed fixture intent"));
  var invalidFacts=w.PreviewContentRestore(inconsistent);var beforeFailure=Directory.GetDirectories(w.Safe("runs/restore-staging")).Length;
  try{w.RestoreContentCopy(invalidFacts,true);throw new Exception("inconsistent facts activated");}catch(IOException){logs.Add("PASS: inconsistent restored facts fail before activation");}
  Tests.Check(!Directory.Exists(invalidFacts.Destination)&&Directory.GetDirectories(w.Safe("runs/restore-staging")).Length==beforeFailure+1&&w.Load(p.Id).Goal==p.Goal&&File.Exists(inconsistent),"restore failure retains candidate archive and original facts",logs);
  var demo=new Workspace(w.Root,true);var demoProject=demo.Load(demo.List()[0].Id);var demoArchive=demo.CreateFullBackup(demoProject,demo.PreviewFullBackup(demoProject));
  var demoRoot=demo.RestoreContentCopy(demo.PreviewContentRestore(demoArchive),true);var demoReopened=RecoveryStartup.Resolve(w.Root,demoRoot);
  Tests.Check(demoReopened.Demo&&demoReopened.Load(demoProject.Id).Demo&&new Workspace(demoRoot).List().Length==0,"content restore and reopen preserve demo isolation",logs);
  var sensitiveFacts=w.Create("敏感事实夹具","api_key=DEMO_ONLY_123456789");var sensitivePlan=w.PreviewFullBackup(sensitiveFacts);
  Tests.Check(sensitivePlan.ReferenceIssues.Any(x=>x.StartsWith("现行事实")),"excluded canonical content cannot be claimed complete",logs);
  try{w.CreateFullBackup(sensitiveFacts,sensitivePlan);throw new Exception("incomplete canonical backup created");}catch(InvalidOperationException){logs.Add("PASS: sensitive canonical fact exclusion blocks backup creation");}
  p.Knowledge.Add(new Fact{Id="K-ref",Text="fixture local reference",Source="docs/reference.md"});w.Save(p,p.Version,"relative material reference");
  Tests.Check(!w.PreviewFullBackup(p).ReferenceIssues.Any(x=>x.Contains("K-ref")),"relative knowledge file reference preserved in backup",logs);
  p.Knowledge[0].Source=w.Safe(prefix+"assets/sample.bin");w.Save(p,p.Version,"absolute reference fixture");
  Tests.Check(w.PreviewFullBackup(p).ReferenceIssues.Any(x=>x.Contains("K-ref")&&x.Contains("绝对引用")),"absolute knowledge reference retained without access or silent rewrite",logs);
  t.RelatedFiles.Add("assets/missing.txt");w.Save(p,p.Version,"missing reference fixture");
  Tests.Check(w.PreviewFullBackup(p).ReferenceIssues.Any(x=>x.Contains("missing.txt")),"missing referenced material explicitly reported",logs);
 }
}
