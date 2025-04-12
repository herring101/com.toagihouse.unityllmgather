# Unity LLM Gather

Unity LLM Gather は、プロジェクトのコードファイルや現在のシーン構造を収集し、大規模言語モデル（LLM）へのインプットに適した単一のテキストファイルとしてまとめるための Unity エディタ拡張機能です。

## 機能

- プロジェクト内のコードファイルを収集して単一のテキストファイルにまとめる
- カスタマイズ可能なファイル除外/含むパターン（glob 形式対応）
- 現在のシーンの構造を自動分析し、統合
- 特定の GameObject を無視する設定機能
- 使いやすいエディタ UI でプロファイル管理

## インストール方法

### Package Manager を使用する方法

1. Unity エディタを開き、`Window > Package Manager`を選択
2. `+`ボタンをクリックし、`Add package from git URL...`を選択
3. 以下の URL を入力: `https://github.com/herring101/com.toagihouse.unityllmgather.git`
4. `Add`をクリックしてインストール

### 手動インストール

1. このリポジトリを ZIP でダウンロードまたはクローン
2. Unity プロジェクトの`Packages`フォルダ内に展開（または`Assets`内の任意のフォルダ）

## 使い方

1. Unity エディタで `Tools > Unity LLM Gather` を選択
2. 設定画面で、以下の項目をカスタマイズ：
   - 対象ディレクトリ（デフォルトは "Assets"）
   - 出力ファイル名（デフォルトは "ProjectSummary.md"）
   - ファイル除外/含むパターン
   - シーンサマリーの生成有無
3. "Generate Summary" ボタンをクリックして実行
4. 指定した出力ファイルがプロジェクトルートに生成されます

## パターンプロファイル

異なるプロジェクトや用途に応じて、ファイルフィルタリングのパターンを複数プロファイルとして管理できます：

- **Exclude Patterns**: 収集から除外するファイルパターン
- **Skip Content Patterns**: 構造には含めるが内容を取得しないファイルパターン
- **Include Patterns**: 特定のファイルのみを収集する場合のパターン

## ライセンス

MIT License

## 作者

[herring101](https://github.com/herring101)
