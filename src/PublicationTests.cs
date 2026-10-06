using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Workbench;
public static class PublicationTests {
 static void Git(Workspace w,string repo,params string[] args){var r=Workspace.GitProcess(repo,w.Root,args);if(r.Code!=0)throw new IOException("publication fixture Git: "+args[0]);}
 public static ProjectRecord Fixture(Workspace w){
  var p=GitAssetTests.Fixture(w);var repo=w.Safe("data/"+w.Mode+"/Projects/"+p.Id+"/repo");
  Git(w,repo,"add","--",".");Git(w,repo,"-c","user.name=Workbench Fixture","-c","user.email=fixture@example.invalid","-c","commit.gpgsign=false","commit","-m","isolated fixture canonical docs baseline");return p;
 }
 public static async Task Run(Workspace w,List<string> logs){
  var p=Fixture(w);var s=new PublicationService(w);string subject=w.Mode+"/"+p.Id;var repo=s.Repo(subject);
  var index=File.ReadAllBytes(Path.Combine(repo,".git/index"));var head=File.ReadAllBytes(Path.Combine(repo,".git/HEAD"));var config=File.ReadAllBytes(Path.Combine(repo,".git/config"));
  var r=await s.Inspect(subject,CancellationToken.None);
  Tests.Check(r.Worktree=="committed_clean"&&r.RemoteState=="unknown"&&r.Files.Count>=2,"publication actual fixture clean local is not remote synced",logs);
  Tests.Check((await s.Inspect(subject,CancellationToken.None)).Stamp==r.Stamp,"publication repeat inspection stable",logs);
  Tests.Check(File.ReadAllBytes(Path.Combine(repo,".git/index")).SequenceEqual(index)&&File.ReadAllBytes(Path.Combine(repo,".git/HEAD")).SequenceEqual(head)&&File.ReadAllBytes(Path.Combine(repo,".git/config")).SequenceEqual(config),"publication read does not alter index HEAD or config",logs);
  Tests.Check(PublicationService.GithubTarget("https://github.com/FixtureOwner/FixtureRepo.git")=="FixtureOwner/FixtureRepo"&&PublicationService.GithubTarget("git@github.com:FixtureOwner/FixtureRepo.git")=="FixtureOwner/FixtureRepo","publication GitHub URL parsing actual pure logic",logs);
  Tests.Check(new[]{"https://token@github.com/a/b.git","https://example.invalid/a/b","file:///outside","ext::command","https://github.com/a/../b"}.All(x=>PublicationService.GithubTarget(x)==""),"publication rejects credential URL external host path and transport",logs);
  var export=await s.Export(r,new[]{r.Files[0].Path},CancellationToken.None);var plan=JsonSerializer.Deserialize<PublishPlan>(File.ReadAllText(w.Safe(export)))!;
  Tests.Check(!plan.CanExecute&&plan.State=="blocked_or_review_required"&&plan.Report.Files.Count==1&&plan.SelectedFiles.Count==1&&plan.TestEvidenceSha256=="not_registered"&&plan.ExternalKeyboardAndDpi=="not_run","publication export exact selection and honest unrun evidence",logs);
  File.WriteAllText(w.Safe("runs/publication-test-evidence.txt"),"isolated test bytes; no pass claim");
  r.Evidence.Add(s.Evidence(r,"test","runs/publication-test-evidence.txt"));
  Tests.Check(r.Evidence[0].Sha256.Length==64&&r.Evidence[0].AssociatedCommit==r.Commit&&r.Evidence[0].Verdict=="unverified_manual_association","publication evidence hash binds bytes to observed source without claiming pass",logs);
  File.AppendAllText(w.Safe("runs/publication-test-evidence.txt"),"changed");
  try{await s.Export(r,new[]{r.Files[0].Path},CancellationToken.None);throw new Exception("changed evidence exported");}catch(IOException){logs.Add("PASS: publication changed evidence blocks export");}r.Evidence.Clear();
  try{s.Evidence(r,"test","runs/../docs/private-fixture");throw new Exception("evidence crossed");}catch(IOException){logs.Add("PASS: publication rejects external evidence path");}
  var second=await s.Export(r,new[]{r.Files[0].Path},CancellationToken.None);Tests.Check(second!=export&&File.Exists(w.Safe(export)),"publication repeated exports preserve original",logs);
  var src=Path.Combine(repo,"src/engine.cs");File.WriteAllText(src,"fixture staged text");Git(w,repo,"add","--","src/engine.cs");File.WriteAllText(src,"fixture working text");
  var dirty=await s.Inspect(subject,CancellationToken.None);var f=dirty.Files.Single(x=>x.Path=="src/engine.cs");
  Tests.Check(dirty.Worktree=="uncommitted"&&f.Status=="MM"&&f.Diff.Contains("fixture staged text")&&f.Diff.Contains("fixture working text"),"publication separately previews actual index and working diffs",logs);
  try{await s.Export(r,new[]{r.Files[0].Path},CancellationToken.None);throw new Exception("stale exported");}catch(IOException){logs.Add("PASS: publication stale preview rejected before export");}
  File.WriteAllText(Path.Combine(repo,"src/private-fixture.txt"),"api_key=DEMO_ONLY_NOT_A_REAL_KEY\nC:\\Users\\FictionalFixture\\private");
  File.WriteAllText(Path.Combine(repo,".env"),"fictional-only");Directory.CreateDirectory(Path.Combine(repo,"obj"));File.WriteAllText(Path.Combine(repo,"obj/cache.fixture"),"cache fixture");
  using(var large=File.Create(Path.Combine(repo,"large.fixture")))large.SetLength(5*1024*1024+1);
  var risk=await s.Inspect(subject,CancellationToken.None);
  Tests.Check(risk.Files.Single(x=>x.Path=="src/private-fixture.txt").Risks.Count>0&&!risk.Files.Single(x=>x.Path=="src/private-fixture.txt").Diff.Contains("DEMO_ONLY_NOT_A_REAL_KEY"),"publication synthetic secret/private line hidden in diff",logs);
  Tests.Check(risk.Files.Single(x=>x.Path==".env").Risks.Count>0&&risk.Files.Single(x=>x.Path=="obj/cache.fixture").Risks.Count>0&&risk.Files.Single(x=>x.Path=="large.fixture").WorkingSha256=="not_read_large","publication sensitive cache and large file blockers",logs);
  using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{await s.Inspect(subject,cancelled.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){logs.Add("PASS: publication cancelled token creates no accepted result");}}
  s.SaveReport(risk);var restarted=new PublicationService(new Workspace(w.Root,w.Demo));Tests.Check(restarted.LoadReport(subject)?.Stamp==risk.Stamp,"publication persisted report readable after new workspace instance",logs);
  var other=Fixture(w);var otherSubject=w.Mode+"/"+other.Id;Tests.Check(s.LoadReport(otherSubject)==null&&(await s.Inspect(otherSubject,CancellationToken.None)).Subject!=risk.Subject,"publication persisted state isolated per project",logs);
  var noGit=w.Create("发布空仓库夹具","只验证本项目缺少 Git 状态");try{await s.Inspect(w.Mode+"/"+noGit.Id,CancellationToken.None);throw new Exception("parent repo discovered");}catch(IOException){logs.Add("PASS: publication missing own Git is error, no parent discovery");}
  try{s.Repo((w.Demo?"local":"examples")+"/"+p.Id);throw new Exception("mode crossed");}catch(IOException){logs.Add("PASS: publication cross mode scope rejected");}
  var configPath=Path.Combine(repo,".git/config");File.AppendAllText(configPath,"\n[include]\npath = fixture-only-not-followed\n");try{await s.Inspect(subject,CancellationToken.None);throw new Exception("include accepted");}catch(IOException){logs.Add("PASS: publication unsafe metadata rejected");}File.WriteAllBytes(configPath,config);
  Git(w,repo,"remote","add","origin","https://github.com/FixtureOwner/FixtureRepo.git");
  Git(w,repo,"config","remote.origin.pushurl","https://github.com/AnotherFixture/Other.git");var mismatch=await s.Inspect(subject,CancellationToken.None);Tests.Check(mismatch.Repository==""&&mismatch.Warnings.Any(x=>x.Contains("pushurl")),"publication differing push target blocks binding",logs);
  // Remote-state test inputs are synthetic SHA observations, not a live GitHub integration.
  var prior=new PublishReport{Subject=subject,Repository="FixtureOwner/FixtureRepo",Branch="fixture",MetadataHash="same",RemoteSha=new string('c',40),RemoteObservedAt=DateTime.UtcNow.ToString("O"),Account="FixtureOwner",RemoteState="synced"};
  var failure=new PublishReport{Subject=subject,Repository=prior.Repository,Branch=prior.Branch,MetadataHash=prior.MetadataHash};PublicationService.CarryRemoteCache(prior,failure);
  Tests.Check(failure.RemoteState=="unknown"&&failure.LastRemoteSha==prior.RemoteSha&&failure.LastRemoteObservedAt==prior.RemoteObservedAt,"publication cached successful SHA is separate from current unknown (synthetic observation)",logs);
  string a=new string('a',40),b=new string('b',40);
  Tests.Check(PublicationService.RemoteRelation(a,a,true,true,true)=="synced"&&PublicationService.RemoteRelation(a,b,false,false,false)=="unknown"&&PublicationService.RemoteRelation(a,b,true,true,false)=="committed_unpushed"&&PublicationService.RemoteRelation(a,b,true,false,true)=="behind"&&PublicationService.RemoteRelation(a,b,true,false,false)=="diverged","publication remote relation pure synthetic matrix (not GitHub integration)",logs);
  try{s.Bind(r);throw new Exception("unverified bound");}catch(IOException){logs.Add("PASS: publication refuses unverified account target binding");}
  var synthetic=new PublishReport{Subject=subject,Repository="FixtureOwner/FixtureRepo",Branch="fixture",Account="FixtureOwner",Visibility="public",MetadataHash="fixture",RemoteObservedAt=DateTime.UtcNow.ToString("O")};var binding=s.Bind(synthetic);
  Tests.Check(PublicationService.BindingMatches(synthetic,s.LoadBinding(subject))&&s.LoadBinding(otherSubject)==null,"publication synthetic verified binding persistence and isolation (not live auth)",logs);
  synthetic.Branch="different-fixture";Tests.Check(!PublicationService.BindingMatches(synthetic,binding),"publication changed branch invalidates binding",logs);
  Git(w,repo,"-c","user.name=Workbench Fixture","-c","user.email=fixture@example.invalid","-c","commit.gpgsign=false","commit","-m","fixture actual descendant");
  string descendant=Workspace.GitProcess(repo,w.Root,"rev-parse","HEAD").Output.Trim();
  bool beforeIsAncestor=Workspace.GitProcess(repo,w.Root,"merge-base","--is-ancestor",r.Commit,descendant).Code==0;
  bool afterIsAncestor=Workspace.GitProcess(repo,w.Root,"merge-base","--is-ancestor",descendant,r.Commit).Code==0;
  Tests.Check(PublicationService.RemoteRelation(descendant,r.Commit,true,beforeIsAncestor,afterIsAncestor)=="committed_unpushed"&&PublicationService.RemoteRelation(r.Commit,descendant,true,afterIsAncestor,beforeIsAncestor)=="behind","publication relation from real isolated Git ancestor objects (not live remote)",logs);
  string tree=Workspace.GitProcess(repo,w.Root,"rev-parse","HEAD^{tree}").Output.Trim();
  string sibling=Workspace.GitProcess(repo,w.Root,"-c","user.name=Workbench Fixture","-c","user.email=fixture@example.invalid","commit-tree",tree,"-p",r.Commit,"-m","isolated sibling fixture").Output.Trim();
  Tests.Check(sibling.Length==40&&PublicationService.RemoteRelation(descendant,sibling,true,Workspace.GitProcess(repo,w.Root,"merge-base","--is-ancestor",sibling,descendant).Code==0,Workspace.GitProcess(repo,w.Root,"merge-base","--is-ancestor",descendant,sibling).Code==0)=="diverged","publication divergence from actual local Git sibling commits (not live remote)",logs);
  logs.Add("NOT RUN: automated live GitHub fault/auth/network scenarios; synthetic relation and binding matrix are unit tests only");
 }
}
