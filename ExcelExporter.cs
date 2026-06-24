using System;
using System.Data;
using System.Windows;
using Excel = Microsoft.Office.Interop.Excel;

namespace Rental
{
    public class ExcelExporter
    {
        public static void ExportToExcel(System.Data.DataTable data, string sheetName, string title, string period)
        {
            if (data == null || data.Rows.Count == 0)
            {
                MessageBox.Show("Нет данных для экспорта!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Создаем приложение Excel
                Excel.Application excelApp = new Excel.Application();
                excelApp.Visible = true;
                excelApp.DisplayAlerts = false;

                // Добавляем новую книгу
                Excel.Workbook workbook = excelApp.Workbooks.Add(Type.Missing);
                Excel.Worksheet worksheet = (Excel.Worksheet)workbook.Worksheets[1];
                worksheet.Name = sheetName.Length > 31 ? sheetName.Substring(0, 31) : sheetName;

                // Заголовок отчета
                Excel.Range titleRange = worksheet.Cells[1, 1];
                titleRange.Value = title;
                titleRange.Font.Bold = true;
                titleRange.Font.Size = 16;
                titleRange.Font.Name = "Times New Roman";
                titleRange.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;

                Excel.Range titleMergeRange = worksheet.Range[worksheet.Cells[1, 1], worksheet.Cells[1, data.Columns.Count]];
                titleMergeRange.Merge();

                // Период
                Excel.Range periodRange = worksheet.Cells[2, 1];
                periodRange.Value = period;
                periodRange.Font.Size = 11;
                periodRange.Font.Name = "Times New Roman";
                periodRange.Font.Italic = true;

                Excel.Range periodMergeRange = worksheet.Range[worksheet.Cells[2, 1], worksheet.Cells[2, data.Columns.Count]];
                periodMergeRange.Merge();

                // Заголовки колонок
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    Excel.Range cell = worksheet.Cells[4, i + 1];
                    cell.Value = data.Columns[i].ColumnName;
                    cell.Font.Bold = true;
                    cell.Font.Size = 12;
                    cell.Font.Name = "Times New Roman";
                    cell.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.LightGray);
                    cell.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
                    cell.HorizontalAlignment = Excel.XlHAlign.xlHAlignCenter;
                }

                // Данные - ПРАВИЛЬНОЕ ФОРМАТИРОВАНИЕ ЧИСЕЛ
                for (int i = 0; i < data.Rows.Count; i++)
                {
                    for (int j = 0; j < data.Columns.Count; j++)
                    {
                        Excel.Range cell = worksheet.Cells[i + 5, j + 1];

                        // Проверяем тип данных и форматируем
                        if (data.Columns[j].DataType == typeof(decimal) ||
                            data.Columns[j].DataType == typeof(double) ||
                            data.Columns[j].DataType == typeof(int))
                        {
                            // Числовые значения - вставляем как числа
                            if (data.Rows[i][j] != DBNull.Value && data.Rows[i][j] != null)
                            {
                                cell.Value = Convert.ToDouble(data.Rows[i][j]);
                                cell.NumberFormat = "#,##0.00";
                                cell.HorizontalAlignment = Excel.XlHAlign.xlHAlignRight;
                            }
                            else
                            {
                                cell.Value = 0;
                                cell.NumberFormat = "#,##0.00";
                                cell.HorizontalAlignment = Excel.XlHAlign.xlHAlignRight;
                            }
                        }
                        else
                        {
                            // Текстовые значения
                            cell.Value = data.Rows[i][j]?.ToString() ?? "";
                            cell.HorizontalAlignment = Excel.XlHAlign.xlHAlignLeft;
                        }

                        cell.Font.Size = 11;
                        cell.Font.Name = "Times New Roman";
                        cell.Borders.LineStyle = Excel.XlLineStyle.xlContinuous;
                    }
                }

                // ИТОГОВАЯ СТРОКА
                int lastRow = data.Rows.Count + 5;
                bool hasNumbers = false;
                int firstNumberColumn = -1;

                for (int j = 0; j < data.Columns.Count; j++)
                {
                    if (data.Columns[j].DataType == typeof(decimal) ||
                        data.Columns[j].DataType == typeof(double) ||
                        data.Columns[j].DataType == typeof(int))
                    {
                        hasNumbers = true;
                        if (firstNumberColumn == -1) firstNumberColumn = j;

                        Excel.Range sumCell = worksheet.Cells[lastRow + 1, j + 1];
                        // Правильная формула SUM
                        string startCell = GetColumnLetter(j + 1) + "5";
                        string endCell = GetColumnLetter(j + 1) + lastRow;
                        sumCell.Formula = "=SUM(" + startCell + ":" + endCell + ")";
                        sumCell.Font.Bold = true;
                        sumCell.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.LightYellow);
                        sumCell.NumberFormat = "#,##0.00";
                        sumCell.HorizontalAlignment = Excel.XlHAlign.xlHAlignRight;
                    }
                }

                if (hasNumbers && firstNumberColumn != -1)
                {
                    Excel.Range totalLabelCell = worksheet.Cells[lastRow + 1, 1];
                    totalLabelCell.Value = "ИТОГО:";
                    totalLabelCell.Font.Bold = true;
                    totalLabelCell.Interior.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.LightYellow);
                    totalLabelCell.HorizontalAlignment = Excel.XlHAlign.xlHAlignRight;

                    // Объединяем ячейки для подписи ИТОГО
                    if (firstNumberColumn > 1)
                    {
                        Excel.Range totalMergeRange = worksheet.Range[worksheet.Cells[lastRow + 1, 1], worksheet.Cells[lastRow + 1, firstNumberColumn]];
                        totalMergeRange.Merge();
                        totalMergeRange.HorizontalAlignment = Excel.XlHAlign.xlHAlignRight;
                    }
                }

                // Автоподбор ширины колонок
                worksheet.Columns.AutoFit();

                // Показываем Excel
                excelApp.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при экспорте в Excel: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetColumnLetter(int columnNumber)
        {
            string columnLetter = "";
            while (columnNumber > 0)
            {
                columnNumber--;
                columnLetter = (char)('A' + columnNumber % 26) + columnLetter;
                columnNumber /= 26;
            }
            return columnLetter;
        }
    }
}