using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System;
using System.Windows.Forms;

namespace polygon2contester
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            string polygonZipFolder = dialog.SelectedPath;

            if (polygonZipFolder[polygonZipFolder.Length - 1] != '\\')
                polygonZipFolder += '\\';
            string[] archiveNames = Directory.GetFiles(polygonZipFolder, "*.zip");

            foreach (string archiveName in archiveNames)
            {
                Console.WriteLine(archiveName);

                string workareaPath = polygonZipFolder + Path.GetRandomFileName() + @"\";

                //Распаковываем архив Polygon в рабочую папку
                ZipFile.ExtractToDirectory(archiveName, workareaPath);
                Directory.CreateDirectory(workareaPath + @"contesterArchive");

                //Создаём XML-документ, описывающий задачу
                XElement problemDescriptor = new XElement("Problem", new XAttribute("generator", "Contester 2.4"));

                //Указываем имя задачи в XML-документе
                XDocument xmlPolygon = XDocument.Load(workareaPath + @"problem.xml");
                string problemName = xmlPolygon.Descendants("name").First().Attribute("value").Value;
                problemDescriptor.Add(new XElement("Name", new XAttribute("lang", "ru"), problemName));

                //Указываем лимиты времени в XML-документе
                int timeLimit = int.Parse(xmlPolygon.Descendants("time-limit").First().Value);
                problemDescriptor.Add(new XElement("TimeLimit", new XAttribute("platform", "native"), timeLimit));
                problemDescriptor.Add(new XElement("TimeLimit", new XAttribute("platform", "javavm"), timeLimit));
                problemDescriptor.Add(new XElement("TimeLimit", new XAttribute("platform", "dotnet"), timeLimit));
                problemDescriptor.Add(new XElement("TimeLimit", new XAttribute("platform", "custom"), timeLimit));

                //Указываем лимиты памяти в XML-документе
                int memoryLimit = int.Parse(xmlPolygon.Descendants("memory-limit").First().Value) / 1024;
                problemDescriptor.Add(new XElement("MemoryLimit", new XAttribute("platform", "native"), memoryLimit));
                problemDescriptor.Add(new XElement("MemoryLimit", new XAttribute("platform", "javavm"), memoryLimit));
                problemDescriptor.Add(new XElement("MemoryLimit", new XAttribute("platform", "dotnet"), memoryLimit));
                problemDescriptor.Add(new XElement("MemoryLimit", new XAttribute("platform", "custom"), memoryLimit));

                //Объединяем HTML и CSS условия, копируем изображения (если есть) и указываем условие и изображения в XML-документе
                string statementText = File.ReadAllText(workareaPath + @"statements\.html\russian\problem.html");
                string statementStyle = "<style>\n" + File.ReadAllText(workareaPath + @"statements\.html\russian\problem-statement.css") + "</style>";
                statementStyle = statementStyle.Replace(".problem-statement {", ".problem-statement, .problem-statement p {");
                statementStyle = statementStyle.Replace("    max-width: 1024px;\r\n", "");
                statementText = statementText.Replace("<LINK href=\"problem-statement.css\" rel=\"stylesheet\" type=\"text/css\">", statementStyle);
                int imageId = 0;
                foreach (string file in Directory.GetFiles(workareaPath + @"statements\.html\russian\", "*.png"))
                {
                    File.Copy(file, workareaPath + @"contesterArchive\" + Path.GetFileName(file));
                    statementText = statementText.Replace("src=\"" + Path.GetFileName(file) + "\"", "src=\"/ru/figureimageru?n=" + imageId + "\"");
                    problemDescriptor.Add(new XElement("Figure", new XAttribute("n", imageId++), new XElement("Image", new XAttribute("src", Path.GetFileName(file)))));
                }
                File.WriteAllBytes(workareaPath + @"contesterArchive\statement.htm", Encoding.Convert(Encoding.UTF8, Encoding.GetEncoding("windows-1251"), Encoding.UTF8.GetBytes(statementText)));
                problemDescriptor.Add(new XElement("Statement", new XAttribute("lang", "ru"), new XAttribute("src", "statement.htm")));

                //Генерируем тесты, копируем их и указываем в XML-документе
                ProcessStartInfo testsMaker = new ProcessStartInfo(workareaPath + @"doall.bat");
                testsMaker.WorkingDirectory = workareaPath;
                testsMaker.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(testsMaker).WaitForExit();
                foreach (string file in Directory.GetFiles(workareaPath + @"tests"))
                    File.Copy(file, workareaPath + @"contesterArchive\test_" + Path.GetFileName(file));
                problemDescriptor.Add(new XElement("TestList", new XAttribute("inputmask", "test_*"), new XAttribute("patternmask", "test_*.a")));

                //Патчим testlib.h, вставляем в код чекера, копируем его и указываем в XML-документе
                string testlibCode = File.ReadAllText(workareaPath + @"files\testlib.h");
                testlibCode = Regex.Replace(testlibCode, "OK_EXIT_CODE \\d+", "OK_EXIT_CODE 0xAC");
                testlibCode = Regex.Replace(testlibCode, "WA_EXIT_CODE \\d+", "WA_EXIT_CODE 0xAB");
                testlibCode = Regex.Replace(testlibCode, "PE_EXIT_CODE \\d+", "PE_EXIT_CODE 0xAA");
                testlibCode = Regex.Replace(testlibCode, "FAIL_EXIT_CODE \\d+", "FAIL_EXIT_CODE 0xA3");
                string filesPatch =
                    "void registerTestlibCmd(int argc, char* argv[])\n" +
                    "{\n" +
                    "    __testlib_ensuresPreconditions();\n" +
                    "    testlibMode = _checker;\n" +
                    "    __testlib_set_binary(stdin);\n" +
                    "    inf.init(\"input.txt\", _input);\n" +
                    "    ouf.init(\"output.txt\", _output);\n" +
                    "    ans.init(\"pattern.txt\", _answer);\n" +
                    "}\n";
                testlibCode = Regex.Replace(testlibCode, "void registerTestlibCmd.*?\n}", filesPatch, RegexOptions.Singleline);

                testlibCode = Regex.Replace(testlibCode, "std::max", "__testlib_max", RegexOptions.Singleline);

                string checkerCode = File.ReadAllText(workareaPath + @"check.cpp");
                checkerCode = checkerCode.Replace("#include \"testlib.h\"", testlibCode);
                //File.WriteAllBytes(workareaPath + @"contesterArchive\checker.cpp", Encoding.Convert(Encoding.UTF8, Encoding.GetEncoding("windows-1251"), Encoding.UTF8.GetBytes(checkerCode)));
                File.WriteAllText(workareaPath + @"contesterArchive\checker.cpp", checkerCode);
                problemDescriptor.Add(new XElement("Judge", new XElement("Checker", new XAttribute("src", "checker.cpp"))));

                //Копируем решения и указываем их в XML-документе
                foreach (string file in Directory.GetFiles(workareaPath + @"solutions\", "*.cpp").Concat(Directory.GetFiles(workareaPath + @"solutions\", "*.java")))
                {
                    File.Copy(file, workareaPath + @"contesterArchive\" + Path.GetFileName(file));
                    problemDescriptor.Add(new XElement("Attempt", new XElement("Solver", new XAttribute("src", Path.GetFileName(file)))));
                }

                //Сохраняем XML-документ, описывающий задачу
                new XDocument(new XDeclaration("1.0", "windows-1251", ""), problemDescriptor).Save(workareaPath + @"contesterArchive\problem.xml");

                //Собираем архив для Contester
                if (!Directory.Exists(polygonZipFolder + @"contester\"))
                    Directory.CreateDirectory(polygonZipFolder + @"contester\");
                if (File.Exists(polygonZipFolder + @"contester\" + Path.GetFileName(archiveName)))
                    File.Delete(polygonZipFolder + @"contester\" + Path.GetFileName(archiveName));
                ZipFile.CreateFromDirectory(workareaPath + @"contesterArchive\", polygonZipFolder + @"contester\" + Path.GetFileName(archiveName));

                //Удаляем рабочую папку
                Directory.Delete(workareaPath, true);

            }
        }
    }
}
