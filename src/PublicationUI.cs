using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Workbench;
public partial class MainWindow {
 string publishSubject="";
 PublishReport? publishReport;
 bool publishStale=true;
 bool publishSelf=false;
 bool publishBusy=false;
 CancellationTokenSource? publishCancel;
 int publishGeneration;
 string publishMessage="";
 readonly HashSet<string> publishSelection=new(StringComparer.Ordinal);
 PublicationService Publisher=>new(workspace);
 string PublishSubject=>publishSelf?"self":selected==""?"":workspace.Mode+"/"+selected;
 void InvalidatePublication(){publishCancel?.Cancel();try{if(publishBusy&&publishSubject!="")Publisher.RecordOperation(publishSubject,"cancelled_or_left_page");}catch(IOException){}publishGeneration++;publishBusy=false;publishStale=true;publishMessage="范围已切换或页面已返回，历史检查需要重查";}
 void PublicationPage(){
  string subject=PublishSubject;
  if(subject!=publishSubject){InvalidatePublication();publishSubject=subject;publishReport=subject==""?null:Publisher.LoadReport(subject);publishSelection.Clear();if(publishReport!=null)foreach(var f in publishReport.Files)publishSelection.Add(f.Path);}
  Row(Button("检查当前沙盒项目",()=>{InvalidatePublication();publishSelf=false;Show("发布计划");},false,!publishBusy),Button("检查工作台自身仓库",()=>{InvalidatePublication();publishSelf=true;Show("发布计划");},false,!publishBusy));
  Card("1 · 确定范围",subject==""?"尚未选择沙盒项目。可先新建项目，或显式选择工作台自身 repo。":("范围："+subject+"\n只读取本项目已登记 repo 的 Git；不扫描其他工作区。\n不会提交、推送、合并 main、创建 Release、登录或安装工具。"));
  if(subject=="")return;
  Row(Button(publishBusy?"检查进行中…":"检查本地状态",()=>_ = PublishOperation(false),true,!publishBusy),Button("联网核对（只读）",()=>_ = PublishOperation(true),false,!publishBusy&&publishReport!=null&&!publishStale),Button("取消检查",()=>{InvalidatePublication();Publisher.RecordOperation(subject,"cancelled");publishMessage="检查已取消；未接受未完成结果。已完成落盘的审查文件保留；原 Git 与已有绑定未改动";Show("发布计划");},false,publishBusy));
  if(publishMessage!="")Card("检查消息",publishMessage,publishStale);
  var r=publishReport;
  if(r==null){Card("等待检查","需要 Git 独立仓库。缺少 .git、不可读、链接仓库或未支持配置会明确失败，不判定为干净或同步。");return;}
  Card(publishStale?"2 · 历史检查 · 需要重查":"2 · 本地与远端是两种状态",$"工作区：{r.Worktree}   /   远端：{(publishStale?"current_unknown (历史 "+r.RemoteState+")":r.RemoteState)}\n分支：{r.Branch}\nHEAD：{r.Commit}\n本地观测：{r.ObservedAt}\n远端 SHA：{r.RemoteSha}\n远端观测：{(r.RemoteObservedAt==""?"未核对":r.RemoteObservedAt)}\n{r.RemoteDetail}",publishStale||r.RemoteState=="unknown");
  if(r.LastRemoteObservedAt!=""&&r.RemoteState=="unknown")Card("最后成功的远端观测（仅历史，不代表当前同步）",r.LastRemoteSha+"\n"+r.LastRemoteObservedAt+" / "+r.LastVerifiedAccount,true);
  var binding=Publisher.LoadBinding(subject);bool matched=!publishStale&&PublicationService.BindingMatches(r,binding);
  var tool=new TextBox{Text=Publisher.GithubTool(),MinWidth=260,Margin=new Thickness(0,0,0,5)};AutomationProperties.SetName(tool,"现有 gh 可执行路径");
  var toolPanel=new StackPanel();toolPanel.Children.Add(Text("已有 gh 工具（不安装、不登录）：",12));toolPanel.Children.Add(tool);toolPanel.Children.Add(Button("登记已有 gh 路径",()=>{Publisher.SetGithubTool(tool.Text);publishMessage="工具路径已登记，下次联网核对生效；未修改 PATH 或认证";Show("发布计划");},false,!publishBusy));body.Children.Add(new Expander{Header="现有工具路径",Content=toolPanel});
  Card("目标绑定",$"GitHub：{(r.Repository==""?"未配置 / 不支持 / 存在歧义":r.Repository)}\n账号：{r.Account}   可见性：{r.Visibility}\n绑定：{(matched?"与本次观测一致":binding==null?"尚未确认":"已有绑定需重新核对")}\n绑定只记录目标元数据，不保存 token 或密码；账号由现有 gh 只读核对。");
  Row(Button("确认本次目标绑定",()=>{Publisher.Bind(r);publishMessage="目标绑定已保存；这不是发布授权，也未执行推送";Show("发布计划");},false,!publishBusy&&!publishStale&&r.RemoteObservedAt!=""));
  if(r.Warnings.Count>0)Card("待核对事项",string.Join("\n",r.Warnings),true);
  if(r.UnpushedCommits.Count>0)Card("尚未推送的提交",string.Join("\n",r.UnpushedCommits)+"\n历史秘密审查未完成；本计划无执行能力",true);
  if(r.CommittedDiff!="")body.Children.Add(new Expander{Header="查看远端到候选 HEAD 的已提交差异",Content=new TextBox{Text=r.CommittedDiff,IsReadOnly=true,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
  var list=new StackPanel();list.Children.Add(Text("3 · 精确文件范围",18,"#233B5A",true));
  list.Children.Add(Text("选择仅决定导出范围。保守规则可能误报，无法保证检出全部秘密；风险项仍须人工审查。",12));
  Row(Button("选择变化文件",()=>{publishSelection.Clear();foreach(var f in r.Files.Where(x=>x.Status!="clean"))publishSelection.Add(f.Path);Show("发布计划");},false,!publishBusy),Button("选择全部受检文件",()=>{publishSelection.Clear();foreach(var f in r.Files)publishSelection.Add(f.Path);Show("发布计划");},false,!publishBusy));
  foreach(var f in r.Files){
   var item=new StackPanel();var choice=new CheckBox{Content=f.Path+"  ["+f.Status+"]",IsChecked=publishSelection.Contains(f.Path),IsEnabled=!publishBusy&&!publishStale,Margin=new Thickness(2,5,2,4)};AutomationProperties.SetName(choice,"发布范围文件 "+f.Path);
   choice.Checked+=(_,__)=>{publishSelection.Add(f.Path);UpdateExportButton();};choice.Unchecked+=(_,__)=>{publishSelection.Remove(f.Path);UpdateExportButton();};item.Children.Add(choice);
   item.Children.Add(Text("working SHA256："+f.WorkingSha256+"\nindex blob："+(f.IndexBlob==""?"无":f.IndexBlob),11));
   if(f.Risks.Count>0)item.Children.Add(Text("需审查："+string.Join("；",f.Risks),12,"#9A5811",true));
   if(f.Diff!=""){var diff=new TextBox{Text=f.Diff,IsReadOnly=true,TextWrapping=TextWrapping.NoWrap,FontFamily=new System.Windows.Media.FontFamily("Consolas"),FontSize=12,MaxHeight=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};item.Children.Add(new Expander{Header="查看 index / working 精确 diff（敏感行已隐藏）",Content=diff});}
   list.Children.Add(new Border{Child=item,Background=Color("#FFFFFF"),Padding=new Thickness(12),Margin=new Thickness(0,0,0,7),CornerRadius=new CornerRadius(7)});
  }
  body.Children.Add(new ScrollViewer{Content=list,MaxHeight=300,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  Card("4 · 候选与验收",$"候选 HEAD：{r.Commit}\n受检工作区指纹：{r.Stamp}\n测试证据 SHA256：{r.Evidence.FirstOrDefault(x=>x.Kind=="test")?.Sha256??"not_registered"}\n候选 ZIP SHA256：{r.Evidence.FirstOrDefault(x=>x.Kind=="package")?.Sha256??"not_registered"}\n外部键鼠 / 实际 DPI：not_run\nmain 合并 / Release 发布：not_run\n哈希只标识字节，不意味着测试通过；本面板不自动读取旧日志来充当当前候选证据。",true);
  var evidencePanel=new StackPanel();foreach(var kind in new[]{"test","package"}){var input=new TextBox{Text=r.Evidence.FirstOrDefault(x=>x.Kind==kind)?.Path??"",Margin=new Thickness(0,5,0,5)};AutomationProperties.SetName(input,kind+" 本项目相对证据路径");evidencePanel.Children.Add(Text(kind+"：本工作区 runs/ 或 releases/ 相对文件路径",12));evidencePanel.Children.Add(input);evidencePanel.Children.Add(Button("登记"+(kind=="test"?"测试证据":"候选包")+"哈希",()=>{var evidence=Publisher.Evidence(r,kind,input.Text);r.Evidence.RemoveAll(x=>x.Kind==kind);r.Evidence.Add(evidence);Publisher.SaveReport(r);publishMessage="已关联字节哈希与当前来源指纹；测试结果/构建来源仍是人工未验证声明";Show("发布计划");},false,!publishBusy&&!publishStale));}
  body.Children.Add(new Expander{Header="可选：关联本项目证据与候选包（不会判为通过）",Content=evidencePanel});
  Row(Button("导出审查计划",()=>_ = ExportPublication(),true,!publishBusy&&!publishStale&&publishSelection.Count>0),Button("返回项目总览",()=>Show("项目总览")));
 }
 async Task PublishOperation(bool online){
  if(publishBusy)return;string subject=PublishSubject;if(subject=="")return;
  publishCancel?.Dispose();publishCancel=new CancellationTokenSource();var token=publishCancel.Token;int generation=++publishGeneration;var old=publishReport;
  publishBusy=true;publishStale=true;publishMessage=online?"正在只读核对已有 gh 账号与指定目标…":"正在读取本项目 Git、index 与工作文件…";Publisher.RecordOperation(subject,"running");Show("发布计划");
  try{
   var result=await Task.Run(async()=>online&&old!=null?await Publisher.ObserveGithub(old,token):await Publisher.Inspect(subject,token),token);
   if(generation!=publishGeneration||PublishSubject!=subject||page!="发布计划")return;
   Publisher.SaveReport(result);publishReport=result;publishSelection.Clear();foreach(var f in result.Files.Where(x=>x.Status!="clean"))publishSelection.Add(f.Path);if(publishSelection.Count==0)foreach(var f in result.Files)publishSelection.Add(f.Path);
   publishStale=false;publishMessage=online?"只读远端核对已结束；请查看独立远端状态与观测时间":"本地检查完成；远端仍是 unknown，需显式联网核对";Publisher.RecordOperation(subject,"completed");
  }catch(OperationCanceledException){if(generation==publishGeneration){publishMessage="检查已取消；不接受未完成结果";Publisher.RecordOperation(subject,"cancelled");}}
  catch(Exception){if(generation==publishGeneration){publishMessage="检查失败或状态发生变化；未判定为干净/同步。请核对 .git、Git 工具、支持范围后重查";publishStale=true;Publisher.RecordOperation(subject,"failed");}}
  finally{if(generation==publishGeneration){publishBusy=false;if(page=="发布计划")Show("发布计划");}}
 }
 async Task ExportPublication(){
  if(publishBusy||publishStale||publishReport==null)return;string subject=PublishSubject;int generation=++publishGeneration;publishCancel?.Dispose();publishCancel=new CancellationTokenSource();var token=publishCancel.Token;var r=publishReport;var scope=publishSelection.ToArray();publishBusy=true;publishMessage="导出前重新核对本地状态…";Show("发布计划");
  try{var path=await Task.Run(()=>Publisher.Export(r,scope,token),token);if(generation==publishGeneration){publishMessage="审查计划已写入："+path+"\n仅供核对；包含阻断/未验证状态，不包含推送执行能力。";}}
  catch(OperationCanceledException){if(generation==publishGeneration)publishMessage="导出已取消";}
  catch(Exception){if(generation==publishGeneration){publishStale=true;publishMessage="导出未完成：源状态变化或文件不可读。旧预览已失效，请重新检查";}}
  finally{if(generation==publishGeneration){publishBusy=false;if(page=="发布计划")Show("发布计划");}}
 }
 void UpdateExportButton(){if(actions.TryGetValue("导出审查计划",out var button))button.IsEnabled=!publishBusy&&!publishStale&&publishSelection.Count>0;}
 public Task CheckPublicationForTest(bool online=false)=>PublishOperation(online);
 public Task ExportPublicationForTest()=>ExportPublication();
 public void ScrollPublicationForTest(double offset){Flatten((System.Windows.DependencyObject)Content).OfType<ScrollViewer>().First(x=>ReferenceEquals(x.Content,body)).ScrollToVerticalOffset(offset);UpdateLayout();}
 public bool PublicationStaleForTest=>publishStale;
}
