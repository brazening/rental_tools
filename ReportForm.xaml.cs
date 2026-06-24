using System;
using System.Data;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using Npgsql;

namespace Rental
{
    public partial class ReportForm : Window
    {
        private string _connectionString;
        private DataTable _currentData;
        private string _currentReportTitle;
        private List<ToolItem> _toolsList;

        public ReportForm()
        {
            InitializeComponent();

            var app = (App)Application.Current;
            _connectionString = app.connString;

            dpStartDate.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dpEndDate.SelectedDate = DateTime.Today;

            // Добавляем обработчик для форматирования колонок с датами
            dgReport.AutoGeneratingColumn += DgReport_AutoGeneratingColumn;

            LoadTools();

            cmbTool.IsEnabled = false;
        }

        // Форматирование колонок с датами
        private void DgReport_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyType == typeof(DateTime) || e.PropertyType == typeof(DateTime?))
            {
                var textColumn = e.Column as DataGridTextColumn;
                if (textColumn != null && textColumn.Binding is System.Windows.Data.Binding binding)
                {
                    textColumn.Binding.StringFormat = "dd.MM.yyyy";
                }
            }
        }

        private void LoadTools()
        {
            try
            {
                var toolsList = new List<ToolComboBoxItem>();

                // Добавляем пункт "Все инструменты"
                toolsList.Add(new ToolComboBoxItem
                {
                    Id = -1,
                    InventoryNumber = "🔧",
                    ModelName = "Все инструменты",
                    IsAllTools = true
                });

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    string query = @"
                SELECT t.id, t.inventory_number, tm.name as model_name
                FROM public.tools t
                JOIN public.tool_models tm ON t.model_id = tm.id
                WHERE t.status != 'списан'
                ORDER BY tm.name, t.inventory_number";

                    var cmd = new NpgsqlCommand(query, conn);
                    var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        toolsList.Add(new ToolComboBoxItem
                        {
                            Id = reader.GetInt32(0),
                            InventoryNumber = reader.GetString(1),
                            ModelName = reader.GetString(2),
                            IsAllTools = false
                        });
                    }
                }

                cmbTool.ItemsSource = toolsList;
                // УДАЛИТЕ ЭТУ СТРОКУ:
                // cmbTool.DisplayMemberPath = "DisplayText";

                if (toolsList.Count > 0)
                {
                    cmbTool.SelectedIndex = 0; // Выбираем "Все инструменты" по умолчанию
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки инструментов: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetPeriodText()
        {
            return $"Период: {dpStartDate.SelectedDate.Value:dd.MM.yyyy} - {dpEndDate.SelectedDate.Value:dd.MM.yyyy}";
        }

        private void ShowToolInfo(DataTable toolData)
        {
            if (toolData == null || toolData.Rows.Count == 0)
            {
                borderToolInfo.Visibility = Visibility.Collapsed;
                return;
            }

            var row = toolData.Rows[0];
            var infoLines = new List<string>();

            infoLines.Add($"🔧 Инвентарный номер: {row["Инвентарный номер"]}");
            infoLines.Add($"📌 Модель: {row["Модель"]}");
            infoLines.Add($"💰 Цена аренды (день): {row["Цена аренды (день)"]} руб.");
            infoLines.Add($"📊 Текущий статус: {row["Текущий статус"]}");
            infoLines.Add($"🔧 Состояние: {row["Состояние"]}");
            infoLines.Add($"🏪 Текущий склад: {row["Текущий склад"]}");
            infoLines.Add($"💵 Цена покупки: {row["Цена покупки"]} руб.");

            // Форматируем дату покупки
            if (row["Дата покупки"] != DBNull.Value && row["Дата покупки"] != null)
            {
                DateTime purchaseDate;
                if (DateTime.TryParse(row["Дата покупки"].ToString(), out purchaseDate))
                {
                    infoLines.Add($"📅 Дата покупки: {purchaseDate:dd.MM.yyyy}");
                }
                else
                {
                    infoLines.Add($"📅 Дата покупки: {row["Дата покупки"]}");
                }
            }
            else
            {
                infoLines.Add($"📅 Дата покупки: Не указана");
            }

            infoLines.Add($"⏱️ Срок службы: {row["Срок службы (мес)"]} мес.");
            infoLines.Add($"");
            infoLines.Add($"📊 СТАТИСТИКА ИСПОЛЬЗОВАНИЯ:");
            infoLines.Add($"└ Всего аренд: {row["Всего аренд"]}");
            infoLines.Add($"└ Общая выручка: {Convert.ToDecimal(row["Общая выручка (руб)"]):N2} руб.");
            infoLines.Add($"└ Средняя длительность аренды: {row["Средняя длительность аренды (дней)"]} дней");
            infoLines.Add($"└ Количество повреждений: {row["Количество повреждений"]}");
            infoLines.Add($"└ Количество просрочек: {row["Количество просрочек"]}");

            itemsToolInfo.ItemsSource = infoLines;
            borderToolInfo.Visibility = Visibility.Visible;
            dgReport.Height = 350;
        }

        private void HideToolInfo()
        {
            borderToolInfo.Visibility = Visibility.Collapsed;
            dgReport.Height = double.NaN;
        }

        // 1. Рейтинг моделей инструментов
        private void btnRatingModels_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateDates()) return;
            cmbTool.IsEnabled = false;
            HideToolInfo();

            try
            {
                string query = @"
            SELECT 
                tm.name AS ""Модель"",
                c.name AS ""Категория"",
                COUNT(DISTINCT ri.id) AS ""Количество аренд"",
                COALESCE(SUM(ri.price_per_day * (r.end_date::date - r.start_date::date)), 0) AS ""Общая выручка"",
                RANK() OVER (ORDER BY COUNT(DISTINCT ri.id) DESC) AS ""Место""
            FROM public.tool_models tm
            LEFT JOIN public.categories c ON tm.category_id = c.id
            LEFT JOIN public.tools t ON tm.id = t.model_id
            LEFT JOIN public.rental_items ri ON t.id = ri.tool_id
            LEFT JOIN public.rentals r ON ri.rental_id = r.id
                AND r.start_date BETWEEN @startDate AND @endDate
                AND r.status = 'завершена'
            GROUP BY tm.id, tm.name, c.name
            HAVING COUNT(DISTINCT ri.id) > 0
            ORDER BY ""Количество аренд"" DESC";

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@startDate", dpStartDate.SelectedDate.Value.Date);
                        cmd.Parameters.AddWithValue("@endDate", dpEndDate.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1));

                        var adapter = new NpgsqlDataAdapter(cmd);
                        _currentData = new DataTable();
                        adapter.Fill(_currentData);
                    }
                }

                dgReport.ItemsSource = _currentData.DefaultView;
                _currentReportTitle = $"Рейтинг моделей инструментов";
                btnExportExcel.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 2. Продажи сотрудников
        private void btnEmployeeSales_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateDates()) return;
            cmbTool.IsEnabled = false;
            HideToolInfo();

            try
            {
                string query = @"
            SELECT 
                e.fio AS ""ФИО сотрудника"",
                e.role AS ""Должность"",
                COUNT(DISTINCT r.id) AS ""Количество договоров"",
                COUNT(DISTINCT ri.id) AS ""Количество арендованных инструментов"",
                COALESCE(SUM(r.total_price), 0) AS ""Сумма продаж""
            FROM public.employees e
            LEFT JOIN public.rentals r ON e.id = r.employee_id 
                AND r.start_date BETWEEN @startDate AND @endDate
                AND r.status = 'завершена'
            LEFT JOIN public.rental_items ri ON r.id = ri.rental_id
            GROUP BY e.id, e.fio, e.role
            HAVING COUNT(DISTINCT r.id) > 0
            ORDER BY ""Сумма продаж"" DESC";

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@startDate", dpStartDate.SelectedDate.Value.Date);
                        cmd.Parameters.AddWithValue("@endDate", dpEndDate.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1));

                        var adapter = new NpgsqlDataAdapter(cmd);
                        _currentData = new DataTable();
                        adapter.Fill(_currentData);
                    }
                }

                dgReport.ItemsSource = _currentData.DefaultView;
                _currentReportTitle = $"Продажи сотрудников";
                btnExportExcel.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 3. Поставки за период
        private void btnSuppliesByPeriod_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateDates()) return;
            cmbTool.IsEnabled = false;
            HideToolInfo();

            try
            {
                string query = @"
                    SELECT 
                        s.supply_number AS ""Номер поставки"",
                        sup.name AS ""Поставщик"",
                        s.supply_date AS ""Дата начала"",
                        s.invoice_date AS ""Дата окончания"",
                        s.status AS ""Статус"",
                        e.fio AS ""Ответственный"",
                        s.total_amount AS ""Сумма поставки"",
                        COUNT(si.id) AS ""Количество позиций""
                    FROM public.supplies s
                    LEFT JOIN public.suppliers sup ON s.supplier_id = sup.id
                    LEFT JOIN public.employees e ON s.employee_id = e.id
                    LEFT JOIN public.supply_items si ON s.id = si.supply_id
                    WHERE s.supply_date BETWEEN @startDate AND @endDate
                    GROUP BY s.id, s.supply_number, sup.name, s.supply_date, s.invoice_date, s.status, e.fio, s.total_amount
                    ORDER BY s.supply_date DESC";

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@startDate", dpStartDate.SelectedDate.Value.Date);
                        cmd.Parameters.AddWithValue("@endDate", dpEndDate.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1));

                        var adapter = new NpgsqlDataAdapter(cmd);
                        _currentData = new DataTable();
                        adapter.Fill(_currentData);
                    }
                }

                dgReport.ItemsSource = _currentData.DefaultView;
                _currentReportTitle = $"Поставки за период";
                btnExportExcel.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 4. Штрафы за период
        private void btnFinesByPeriod_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateDates()) return;
            cmbTool.IsEnabled = false;
            HideToolInfo();

            try
            {
                string query = @"
            SELECT 
                f.type AS ""Тип штрафа"",
                f.amount AS ""Сумма штрафа"",
                f.created_at AS ""Дата начисления"",
                c.name AS ""Клиент"",
                CAST(r.id AS varchar) AS ""№ договора"",
                CASE 
                    WHEN f.type = 'просрочка' THEN 'Просрочка возврата'
                    WHEN f.type = 'повреждение' THEN 'Повреждение инструмента'
                    WHEN f.type = 'потеря' THEN 'Потеря инструмента'
                    ELSE f.type
                END AS ""Описание""
            FROM public.fines f
            LEFT JOIN public.rentals r ON f.rental_id = r.id
            LEFT JOIN public.clients c ON r.client_id = c.id
            WHERE f.created_at BETWEEN @startDate AND @endDate
            ORDER BY f.created_at DESC";

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@startDate", dpStartDate.SelectedDate.Value.Date);
                        cmd.Parameters.AddWithValue("@endDate", dpEndDate.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1));

                        var adapter = new NpgsqlDataAdapter(cmd);
                        _currentData = new DataTable();
                        adapter.Fill(_currentData);
                    }
                }

                dgReport.ItemsSource = _currentData.DefaultView;
                _currentReportTitle = $"Штрафы за период";
                btnExportExcel.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 5. История аренд по выбранному инструменту (или всем)
        private void btnToolHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateDates()) return;

            cmbTool.IsEnabled = true;

            if (cmbTool.SelectedItem == null)
            {
                MessageBox.Show("Выберите инструмент!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedTool = (ToolComboBoxItem)cmbTool.SelectedItem;

            try
            {
                // Если выбран "Все инструменты"
                if (selectedTool.IsAllTools)
                {
                    // Скрываем панель с характеристиками (для всех инструментов она не нужна)
                    HideToolInfo();

                    using (var conn = new NpgsqlConnection(_connectionString))
                    {
                        conn.Open();

                        // Запрос для всех инструментов
                        string allToolsQuery = @"
                    SELECT 
                        t.inventory_number AS ""Инвентарный номер"",
                        tm.name AS ""Модель"",
                        c.name AS ""Клиент"",
                        r.start_date AS ""Дата начала"",
                        r.end_date AS ""Дата окончания"",
                        CASE 
                            WHEN r.actual_return_date IS NOT NULL THEN r.actual_return_date::text
                            ELSE 'Не возвращён'
                        END AS ""Дата возврата"",
                        ri.price_per_day AS ""Цена за день"",
                        (ri.price_per_day * (LEAST(r.end_date, COALESCE(r.actual_return_date, CURRENT_DATE))::date - r.start_date::date))::numeric(10,2) AS ""Сумма"",
                        CASE 
                            WHEN ri.is_damaged = true THEN 'Да'
                            ELSE 'Нет'
                        END AS ""Повреждён"",
                        CASE 
                            WHEN r.actual_return_date > r.end_date THEN 
                                (r.actual_return_date::date - r.end_date::date)::text || ' дн.'
                            ELSE 'Вовремя'
                        END AS ""Просрочка"",
                        e.fio AS ""Оформил""
                    FROM public.rental_items ri
                    JOIN public.rentals r ON ri.rental_id = r.id
                    JOIN public.tools t ON ri.tool_id = t.id
                    JOIN public.tool_models tm ON t.model_id = tm.id
                    LEFT JOIN public.clients c ON r.client_id = c.id
                    LEFT JOIN public.employees e ON r.employee_id = e.id
                    WHERE r.start_date BETWEEN @startDate AND @endDate
                        AND r.status = 'завершена'
                    ORDER BY r.start_date DESC";

                        using (var cmd = new NpgsqlCommand(allToolsQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@startDate", dpStartDate.SelectedDate.Value.Date);
                            cmd.Parameters.AddWithValue("@endDate", dpEndDate.SelectedDate.Value.Date.AddDays(1).AddSeconds(-1));

                            var adapter = new NpgsqlDataAdapter(cmd);
                            _currentData = new DataTable();
                            adapter.Fill(_currentData);
                        }
                    }

                    dgReport.ItemsSource = _currentData.DefaultView;
                    _currentReportTitle = $"История аренд всех инструментов за период: {GetPeriodText()}";
                    btnExportExcel.IsEnabled = true;
                }
                else
                {
                    // Существующий код для одного инструмента
                    DataTable toolInfoData = null;

                    using (var conn = new NpgsqlConnection(_connectionString))
                    {
                        conn.Open();

                        string toolInfoQuery = @"
                    SELECT 
                        t.inventory_number AS ""Инвентарный номер"",
                        tm.name AS ""Модель"",
                        tm.rental_price AS ""Цена аренды (день)"",
                        t.status AS ""Текущий статус"",
                        t.condition_status AS ""Состояние"",
                        COALESCE(s.name, 'Не указан') AS ""Текущий склад"",
                        COALESCE(t.purchase_price, 0) AS ""Цена покупки"",
                        t.purchase_date AS ""Дата покупки"",
                        COALESCE(tm.service_life_months, 0) AS ""Срок службы (мес)"",
                        COALESCE(rental_stats.total_rentals, 0) AS ""Всего аренд"",
                        COALESCE(rental_stats.total_revenue, 0) AS ""Общая выручка (руб)"",
                        COALESCE(rental_stats.avg_rental_days, 0) AS ""Средняя длительность аренды (дней)"",
                        COALESCE(rental_stats.damaged_count, 0) AS ""Количество повреждений"",
                        COALESCE(rental_stats.overdue_count, 0) AS ""Количество просрочек""
                    FROM public.tools t
                    JOIN public.tool_models tm ON t.model_id = tm.id
                    LEFT JOIN public.stock s ON t.stock_id = s.id
                    LEFT JOIN (
                        SELECT 
                            ri.tool_id,
                            COUNT(*) AS total_rentals,
                            SUM(ri.price_per_day * (r.end_date::date - r.start_date::date)) AS total_revenue,
                            AVG((r.end_date::date - r.start_date::date)) AS avg_rental_days,
                            COUNT(CASE WHEN ri.is_damaged = true THEN 1 END) AS damaged_count,
                            COUNT(CASE WHEN r.actual_return_date > r.end_date THEN 1 END) AS overdue_count
                        FROM public.rental_items ri
                        JOIN public.rentals r ON ri.rental_id = r.id
                        WHERE r.status = 'завершена'
                        GROUP BY ri.tool_id
                    ) rental_stats ON rental_stats.tool_id = t.id
                    WHERE t.id = @toolId";

                        using (var cmd = new NpgsqlCommand(toolInfoQuery, conn))
                        {
                            cmd.Parameters.AddWithValue("@toolId", selectedTool.Id);
                            var adapter = new NpgsqlDataAdapter(cmd);
                            toolInfoData = new DataTable();
                            adapter.Fill(toolInfoData);
                        }

                        string historyQuery = @"
                    SELECT 
                        r.start_date AS ""Дата начала"",
                        r.end_date AS ""Дата окончания"",
                        CASE 
                            WHEN r.actual_return_date IS NOT NULL THEN r.actual_return_date::text
                            ELSE 'Не возвращён'
                        END AS ""Дата возврата"",
                        ri.price_per_day AS ""Цена за день"",
                        (ri.price_per_day * (LEAST(r.end_date, COALESCE(r.actual_return_date, CURRENT_DATE))::date - r.start_date::date))::numeric(10,2) AS ""Сумма"",
                        CASE 
                            WHEN ri.is_damaged = true THEN 'Да'
                            ELSE 'Нет'
                        END AS ""Повреждён"",
                        CASE 
                            WHEN r.actual_return_date > r.end_date THEN 
                                (r.actual_return_date::date - r.end_date::date)::text || ' дн.'
                            ELSE 'Вовремя'
                        END AS ""Просрочка"",
                        c.name AS ""Клиент"",
                        e.fio AS ""Оформил""
                    FROM public.rental_items ri
                    JOIN public.rentals r ON ri.rental_id = r.id
                    LEFT JOIN public.clients c ON r.client_id = c.id
                    LEFT JOIN public.employees e ON r.employee_id = e.id
                    WHERE ri.tool_id = @toolId
                    ORDER BY r.start_date DESC";

                        using (var cmdHistory = new NpgsqlCommand(historyQuery, conn))
                        {
                            cmdHistory.Parameters.AddWithValue("@toolId", selectedTool.Id);
                            var historyAdapter = new NpgsqlDataAdapter(cmdHistory);
                            _currentData = new DataTable();
                            historyAdapter.Fill(_currentData);
                        }
                    }

                    ShowToolInfo(toolInfoData);
                    dgReport.ItemsSource = _currentData.DefaultView;
                    _currentReportTitle = $"История аренд инструмента: {selectedTool.InventoryNumber} - {selectedTool.ModelName}";
                    btnExportExcel.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки отчета: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_currentData == null || _currentData.Rows.Count == 0)
            {
                MessageBox.Show("Нет данных для экспорта!", "Предупреждение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ExcelExporter.ExportToExcel(_currentData, "Отчет", _currentReportTitle, GetPeriodText());
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReportTitle != null)
            {
                if (_currentReportTitle.Contains("Рейтинг"))
                    btnRatingModels_Click(sender, e);
                else if (_currentReportTitle.Contains("Продажи"))
                    btnEmployeeSales_Click(sender, e);
                else if (_currentReportTitle.Contains("Поставки"))
                    btnSuppliesByPeriod_Click(sender, e);
                else if (_currentReportTitle.Contains("Штрафы"))
                    btnFinesByPeriod_Click(sender, e);
                else if (_currentReportTitle.Contains("История аренд"))
                    btnToolHistory_Click(sender, e);
            }
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            dgReport.ItemsSource = null;
            _currentData = null;
            _currentReportTitle = null;
            btnExportExcel.IsEnabled = false;
            if (cmbTool.ItemsSource != null && cmbTool.Items.Count > 0)
            {
                cmbTool.SelectedIndex = 0; // Выбираем "Все инструменты"
            }
            cmbTool.IsEnabled = false;
            HideToolInfo();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool ValidateDates()
        {
            if (!dpStartDate.SelectedDate.HasValue)
            {
                MessageBox.Show("Выберите дату начала периода!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!dpEndDate.SelectedDate.HasValue)
            {
                MessageBox.Show("Выберите дату окончания периода!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (dpEndDate.SelectedDate.Value < dpStartDate.SelectedDate.Value)
            {
                MessageBox.Show("Дата окончания не может быть раньше даты начала!", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }
    }

    public class ToolItem
    {
        public int Id { get; set; }
        public string InventoryNumber { get; set; }
        public string ModelName { get; set; }
    }

    public class ToolComboBoxItem
    {
        public int Id { get; set; }
        public string InventoryNumber { get; set; }
        public string ModelName { get; set; }
        public bool IsAllTools { get; set; }

        public string DisplayText
        {
            get
            {
                if (IsAllTools)
                    return "🔧 Все инструменты";
                return $"{InventoryNumber} - {ModelName}";
            }
        }
    }
}