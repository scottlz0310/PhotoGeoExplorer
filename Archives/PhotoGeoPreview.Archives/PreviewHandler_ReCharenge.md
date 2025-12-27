# 開発の経緯

## 各段階で行ったこと・直面した課題

- 1. 初期実装はExplorerに直接統合するように実装したが、Preview Handler が自作 DLL を読み込んでくれない問題 に直面して諦めた。(実装ファイルは消去済み)
- 2. このため次期実装として、Microsoft PowerToysリポジトリをフォークして、File Explorer の Preview Pane に Pic+Map プレビューを組み込む形での実装を行った。
- 3. 実装うまく行ったかのように見えたが、全体ビルドを行うためには非推奨のUWPワークロードをインストールする必要があった。しかしUWPワークロードはMicrosoftが強固に非推奨化していて、Visual Studio 2022以降ではインストールできない。
- 4. そのため、PowerToys全体をビルドすることができず、結果としてPic+Mapプレビューも動作させることができなかった。
- 5. PowerToys本家リポジトリがUWPワークロード依存を解消するまで待つことも考えたが、いつになるか分からないため、再度自作Preview HandlerをExplorerに直接統合する方法での実装を試みることにした。

## 検討すべき再実装案としては以下の2つがある

実は **Windows Shell / COM の Preview Handler 特有の「地獄ポイント」** がいくつかあり、
そこを 1つでも満たせていないと **Explorer が絶対に DLL をロードしてくれません。**という地雷を回避できて居なかった可能性がある。


## 🔵 **失敗の原因、その「正体」の推定。**
 そしてその **回避策**

### 🔥【まず最も多い「読み込まれない理由」TOP10】

- 1. 🟥 ① 64bit Explorer に 32bit DLL を登録していた
**これは一番多い失敗原因。**
* 64bit Windows の Explorer は **64bit の DLL しか読み込まない**
* 32bit DLL を regsvr32 しても *Explorer では一切呼ばれない*

- 2. 🟥 ② regsvr32 の「パス間違い」 or 「WOW64 の罠」
次に多い失敗。
* `C:\Windows\System32\regsvr32.exe` ← 64bit版
* `C:\Windows\SysWOW64\regsvr32.exe` ← **32bit版**
**SysWOW64 の regsvr32 で登録したら絶対 Explorer に無視されます。**
### ✔ 正しい登録方法

```
C:\Windows\System32\regsvr32.exe C:\Your\PreviewHandler.dll
```

---

- 3. 🟥 ③ COM DLL に Preview Handler 用の GUID が正しく書かれていない
Preview Handler は **独自 GUID が3つ**必要です：
1. COM クラス ID
2. Preview Handler の ID
3. 予期する拡張子との紐付け
例：
```
HKLM\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers
  {YOUR-CLASSID-HERE} = "Your Preview Handler Name"
```

- 4. 🟥 ④ DLL が IPreviewHandler ではなく IInitializeWithStream を実装していない

これは仕様ですが、

> **Explorer は IPreviewHandler + IInitializeWithStream を実装していないと絶対に読む気がない**

C# の場合は CsWin32/WinRT を正しく使わないと失敗します。

---

- 5. 🟥 ⑤ STA / MTA スレッドモデル問題

Shell 拡張の DLL は **必ず MTA にしなければならない**。

```
[ProgId("xxx")]
[ComVisible(true)]
[Guid("xxxxx")]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IPreviewHandler))]
public class MyHandler : IPreviewHandler, IInitializeWithStream
{
    // ← このクラスは MTA として登録される必要がある
}
```

`ThreadingModel="Apartment"` が必須。

---

- 6. 🟥 .NET コードの Preview Handler を作るとき「RegAsm では絶対にダメ」

* RegAsm の登録は Explorer に無視される
* OR スレッドモデルが “Both” になって破綻する
* msbuild で `regasm` を使って登録すると動かない

Preview Handler で C# を使うには以下が必要：

* **RegFree COM**
* **.NET Framework 4.8** で COMVisible
* **Native shim DLL**（必須）

C# 単体では **絶対に動きません。**

---

- 7. 🟥 ⑦ .NET 6/7/8 の COM では Shell が認識しない

.NET 6 以降の COM は **レジストリ登録が Win32 COM と互換性がない** ため、Explorer が読みません。

→ 対応策：

* .NET Framework 4.8
* または C++/Win32
* または C# + C++ の Bridge DLL

---

- 8. 🟥 ⑧ プレビュー対象の拡張子が Preview Handler に紐付けられていない

例：

```
HKEY_CLASSES_ROOT\.jpg
   PerceivedType → image
   PreviewHandler → {YOUR CLSID}
```

忘れがち。

---

- 9. 🟥 ⑨ DLL がエラーでロードに失敗している

Explorer は **失敗しても沈黙**するため、気づけない。

### ✔ トレース方法

```
logman start mytrace -p "{c1a0f421-...} 0x5 5"
```

or ETW を有効にして explorer の DLL ロード失敗を確認。

---

- 10. 🟥 ⑩ EXE オブジェクトの所有者（ACL）が壊れていて読み込めない

稀ですが、署名のせいで読み込まれない場合あり。

---

# 🟦 で、どうしたいか（あなたの目的へ戻る）

あなたは：

> **PowerToys 全体ビルドに巻き込まれずに Pic+Map プレビューを動かしたい**

これを実現するためには：

# 🎯 **Preview Handler を「単体で Explorer に読み込ませる方法」を復活させる**

そのために必要なのは：

---

# 🟩 必要なこと（再チャレンジ成功ルート）

## ✔ 1. C++/Win32版 Preview Handler にする

→ これが最も安定・確実
→ Explorer に100%読み込まれる
→ PowerToys も内部で C++ DLL をロードしている理由

---

## ✔ 2. C# の場合は「Native Shim DLL」を作る

理由：

* C# の COM DLL は Shell が直接読み込むには不完全
* InProc COM の制約
* STA/MTA・EntryPoint問題

**PowerToys が C# Preview Handler を動かせるのは “Native Host DLL が存在するから” です。**

---

## ✔ 3. PowerToys の仕組みをそのまま借りれば動く

つまり：

* C++ “GearHost” DLL
* C# の実装 DLL
* RegFreeCOM
* manifest
* PreviewHandlerHost.exe

この構造をそのままシンプルにして使えば **PowerToys 全体不要**。

---

# 🟦 まとめ：

### 初期実装 PreviewHandler DLL が読み込まれなかった理由は

### **Shell が C# DLL を直接ロードしない構造のため（Native Shim 不足）**

### ＋ 他の典型的 COM 罠のどれか。

---

# 🟩 提案：

PicMapPreview を **最小構成で単体動作**させるプロジェクト

構成：

```
PicMapPreview/
 ├─ NativeHost.dll    ← C++/Win32（Explorer はこれをロード）
 ├─ PicMapHandler.dll ← C#（実装本体）
 ├─ app.manifest      ← RegFree COM
 ├─ register.cmd
 ├─ unregister.cmd
```

PowerToys の PreviewHandlerHost の仕組みを簡略化して再利用すれば
**PowerToys 本体は完全に不要で Preview が動く** ようになります。

