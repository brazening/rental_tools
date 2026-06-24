using Npgsql;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;

namespace Rental
{
    public partial class Supplies : Window
    {
        private string connString;
        private ObservableCollection<SupplyView> suppliesList;
        private ObservableCollection<SupplyItemView> supplyItemsList;
        private SupplyView currentSupply;
        private int? editingSupplyId = null;
        private string generatedSupplyNumber = "";
        private bool isEditMode = false;

        public Supplies()
        {
            InitializeComponent();

            connString = (Application.Current as App)?.connString ?? "";

            if (string.IsNullOrEmpty(connString))
            {
                MessageBox.Show("Ошибка: строка подключения не найдена");
                Close();
                return;
            }

            suppliesList = new ObservableCollection<SupplyView>();
            supplyItemsList = new ObservableCollection<SupplyItemView>();

            dgSupplies.ItemsSource = suppliesList;
            dgSupplyItems.ItemsSource = supplyItemsList;

            LoadSuppliers();
            LoadEmployees();
            LoadSupplies();
            GenerateSupplyNumber();
        }

        /// <summary>
        /// Проверка, можно ли добавить инструменты на склад
        /// </summary>
        private bool CanAddToStock(int stockId, int additionalQuantity, int? excludeSupplyId = null)
        {
            string query = @"
                SELECT COUNT(*) 
                FROM public.tools 
                WHERE stock_id = @stockId 
                AND status NOT IN ('списан', 'утерян')";

            try
            {
                using (NpgsqlConnection conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (NpgsqlCommand cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@stockId", stockId);
                        int currentCount = Convert.ToInt32(cmd.ExecuteScalar());

                        string maxQuery = "SELECT max_quantity FROM public.stock WHERE id = @stockId";
                        using (NpgsqlCommand maxCmd = new NpgsqlCommand(maxQuery, conn))
                        {
                            maxCmd.Parameters.AddWithValue("@stockId", stockId);
                            int maxQuantity = Convert.ToInt32(maxCmd.ExecuteScalar());

                            int newCount = currentCount + additionalQuantity;

                            if (newCount > maxQuantity)
                            {
                                MessageBox.Show($"Невозможно добавить инструменты на склад!\n\n" +
                                    $"Склад: \"{GetStockName(stockId)}\"\n" +
                                    $"Текущее количество активных инструментов: {currentCount}\n" +
                                    $"Максимальная вместимость: {maxQuantity}\n" +
                                    $"Попытка добавить: {additionalQuantity} шт.\n" +
                                    $"Будет занято: {newCount} из {maxQuantity}\n" +
                                    $"Не хватает мест: {newCount - maxQuantity}\n\n" +
                                    $"Пожалуйста, измените склад назначения или уменьшите количество!",
                                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return false;
                            }

                            int remaining = maxQuantity - newCount;
                            if (remaining <= 2 && additionalQuantity > 0)
                            {
                                var result = MessageBox.Show($"ВНИМАНИЕ: На складе \"{GetStockName(stockId)}\" после добавления останется всего {remaining} свободных мест из {maxQuantity}.\n\n" +
                                    $"Добавляется: {additionalQuantity} шт.\n" +
                                    $"Текущий запас: {currentCount} шт.\n" +
                                    $"Будет всего: {newCount} шт.\n\n" +
                                    $"Продолжить?",
                                    "Предупреждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

                                return result == MessageBoxResult.Yes;
                            }

                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проверки склада: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Проверка всех позиций поставки на возможность размещения на складах
        /// </summary>
        private bool CanAddAllToStocks(List<SupplyItemView> items)
        {
            // Группируем позиции по складам
            var stockGroups = items.GroupBy(x => x.StockId);

            foreach (var group in stockGroups)
            {
                int stockId = group.Key;
                int totalQuantity = group.Sum(x => x.Quantity);

                if (!CanAddToStock(stockId, totalQuantity))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Получить название склада по ID
        /// </summary>
        private string GetStockName(int stockId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = "SELECT name FROM public.stock WHERE id = @id";
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", stockId);
                        var result = cmd.ExecuteScalar();
                        return result != null ? result.ToString() : "Неизвестный склад";
                    }
                }
            }
            catch
            {
                return "Неизвестный склад";
            }
        }

        private void GenerateSupplyNumber()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand("SELECT MAX(id) FROM public.supplies", conn);
                    var result = cmd.ExecuteScalar();
                    int nextId = (result == DBNull.Value) ? 1 : Convert.ToInt32(result) + 1;
                    generatedSupplyNumber = $"П-{nextId:D6}";
                    txtSupplyNumber.Text = generatedSupplyNumber;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка генерации номера: {ex.Message}");
                generatedSupplyNumber = $"П-{DateTime.Now:yyyyMMddHHmmss}";
                txtSupplyNumber.Text = generatedSupplyNumber;
            }
        }

        private void LoadSuppliers()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand("SELECT id, name FROM public.suppliers ORDER BY name", conn);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    cboSupplier.ItemsSource = dt.AsEnumerable().Select(row => new
                    {
                        Id = row.Field<int>("id"),
                        Name = row.Field<string>("name")
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки поставщиков: {ex.Message}");
            }
        }

        private void LoadEmployees()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    var cmd = new NpgsqlCommand("SELECT id, fio FROM public.employees ORDER BY fio", conn);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    cboEmployee.ItemsSource = dt.AsEnumerable().Select(row => new
                    {
                        Id = row.Field<int>("id"),
                        FIO = row.Field<string>("fio")
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сотрудников: {ex.Message}");
            }
        }

        private void LoadSupplies()
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT s.*, sup.name as SupplierName, e.fio as EmployeeFIO
                        FROM public.supplies s
                        LEFT JOIN public.suppliers sup ON s.supplier_id = sup.id
                        LEFT JOIN public.employees e ON s.employee_id = e.id
                        ORDER BY s.id DESC";

                    var cmd = new NpgsqlCommand(query, conn);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    suppliesList.Clear();
                    foreach (DataRow row in dt.Rows)
                    {
                        suppliesList.Add(new SupplyView
                        {
                            Id = row.Field<int>("id"),
                            SupplyNumber = row.Field<string>("supply_number"),
                            SupplierId = row.IsNull("supplier_id") ? 0 : row.Field<int>("supplier_id"),
                            SupplierName = row.IsNull("SupplierName") ? "" : row.Field<string>("SupplierName"),
                            SupplyDate = row.Field<DateTime>("supply_date"),
                            InvoiceDate = row.IsNull("invoice_date") ? (DateTime?)null : row.Field<DateTime>("invoice_date"),
                            TotalAmount = row.Field<decimal>("total_amount"),
                            Status = row.Field<string>("status"),
                            EmployeeId = row.IsNull("employee_id") ? (int?)null : row.Field<int>("employee_id"),
                            EmployeeFIO = row.IsNull("EmployeeFIO") ? "" : row.Field<string>("EmployeeFIO")
                        });
                    }
                }
                txtStatus.Text = $"Загружено поставок: {suppliesList.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки поставок: {ex.Message}");
            }
        }

        private void LoadSupplyItems(int supplyId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT si.*, tm.name as ToolModelName, s.name as StockName
                        FROM public.supply_items si
                        JOIN public.tool_models tm ON si.tool_model_id = tm.id
                        LEFT JOIN public.stock s ON si.stock_id = s.id
                        WHERE si.supply_id = @supplyId";

                    var cmd = new NpgsqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@supplyId", supplyId);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    supplyItemsList.Clear();
                    foreach (DataRow row in dt.Rows)
                    {
                        supplyItemsList.Add(new SupplyItemView
                        {
                            Id = row.Field<int>("id"),
                            SupplyId = row.Field<int>("supply_id"),
                            ToolModelId = row.Field<int>("tool_model_id"),
                            ToolModelName = row.Field<string>("ToolModelName"),
                            Quantity = row.Field<int>("quantity"),
                            PurchasePrice = row.Field<decimal>("purchase_price"),
                            TotalPrice = row.Field<decimal>("total_price"),
                            StockId = row.IsNull("stock_id") ? 1 : row.Field<int>("stock_id"),
                            StockName = row.IsNull("StockName") ? "" : row.Field<string>("StockName")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки позиций: {ex.Message}");
            }
        }

        private void CalculateTotalAmount(int supplyId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        UPDATE public.supplies 
                        SET total_amount = COALESCE((SELECT SUM(total_price) FROM public.supply_items WHERE supply_id = @supplyId), 0)
                        WHERE id = @supplyId";

                    var cmd = new NpgsqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@supplyId", supplyId);
                    cmd.ExecuteNonQuery();
                }

                LoadSupplies();
                if (currentSupply != null && currentSupply.Id == supplyId)
                {
                    var updated = suppliesList.FirstOrDefault(s => s.Id == supplyId);
                    if (updated != null)
                    {
                        txtTotalAmount.Text = updated.TotalAmount.ToString("N2");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка пересчета суммы: {ex.Message}");
            }
        }

        private void UpdateStatusButtons(string status)
        {
            if (status == "получен" || status == "отменён")
            {
                btnSetDraft.IsEnabled = false;
                btnSetOrdered.IsEnabled = false;
                btnSetReceived.IsEnabled = false;
                btnSetClosed.IsEnabled = false;
            }
            else
            {
                btnSetDraft.IsEnabled = (status != "черновик");
                btnSetOrdered.IsEnabled = (status != "оформлен" && status == "черновик");
                btnSetReceived.IsEnabled = (status != "получен" && status == "оформлен");
                btnSetClosed.IsEnabled = (status != "отменён" && status != "получен");
            }
        }

        private void UpdatePositionButtons(bool hasItems)
        {
            btnEditItem.IsEnabled = hasItems;
            btnDeleteItem.IsEnabled = hasItems;
        }

        private void UpdateCreateButton()
        {
            bool canCreate = cboSupplier.SelectedValue != null &&
                           dpSupplyDate.SelectedDate != null &&
                           cboEmployee.SelectedValue != null &&
                           supplyItemsList.Count > 0;
            btnCreate.IsEnabled = canCreate;
        }

        private void UpdateEditButton()
        {
            btnEditSupply.IsEnabled = currentSupply != null &&
                                      currentSupply.Status == "черновик" &&
                                      currentSupply.Id > 0;
        }

        private void BtnEditSupply_Click(object sender, RoutedEventArgs args)
        {
            if (currentSupply == null)
            {
                MessageBox.Show("Выберите поставку для редактирования");
                return;
            }

            if (currentSupply.Status != "черновик")
            {
                MessageBox.Show("Редактировать можно только поставки в статусе 'Черновик'");
                return;
            }

            int? selectedSupplierId = cboSupplier.SelectedValue as int?;
            int? selectedEmployeeId = cboEmployee.SelectedValue as int?;
            DateTime? selectedDate = dpSupplyDate.SelectedDate;

            if (selectedSupplierId == null || selectedEmployeeId == null || selectedDate == null)
            {
                MessageBox.Show("Заполните все обязательные поля");
                return;
            }

            var result = MessageBox.Show($"Сохранить изменения для поставки {currentSupply.SupplyNumber}?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    using (var conn = new NpgsqlConnection(connString))
                    {
                        conn.Open();
                        string query = @"
                            UPDATE public.supplies 
                            SET supplier_id = @supplierId, 
                                supply_date = @supplyDate, 
                                employee_id = @employeeId
                            WHERE id = @id AND status = 'черновик'";

                        var cmd = new NpgsqlCommand(query, conn);
                        cmd.Parameters.AddWithValue("@supplierId", selectedSupplierId.Value);
                        cmd.Parameters.AddWithValue("@supplyDate", selectedDate.Value);
                        cmd.Parameters.AddWithValue("@employeeId", selectedEmployeeId.Value);
                        cmd.Parameters.AddWithValue("@id", currentSupply.Id);

                        int rowsAffected = cmd.ExecuteNonQuery();

                        if (rowsAffected > 0)
                        {
                            currentSupply.SupplierId = selectedSupplierId.Value;
                            currentSupply.SupplyDate = selectedDate.Value;
                            currentSupply.EmployeeId = selectedEmployeeId.Value;

                            var supplier = ((System.Collections.IEnumerable)cboSupplier.ItemsSource)
                                .Cast<dynamic>().FirstOrDefault(s => s.Id == selectedSupplierId.Value);
                            if (supplier != null)
                                currentSupply.SupplierName = supplier.Name;

                            var employee = ((System.Collections.IEnumerable)cboEmployee.ItemsSource)
                                .Cast<dynamic>().FirstOrDefault(emp => emp.Id == selectedEmployeeId.Value);
                            if (employee != null)
                                currentSupply.EmployeeFIO = employee.FIO;

                            var index = suppliesList.IndexOf(currentSupply);
                            if (index >= 0)
                            {
                                suppliesList[index] = currentSupply;
                            }

                            txtStatus.Text = $"Поставка {currentSupply.SupplyNumber} обновлена";
                            MessageBox.Show("Данные поставки успешно обновлены!", "Успех",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("Не удалось обновить данные. Возможно, статус поставки изменился.");
                            LoadSupplies();
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка обновления: {ex.Message}");
                }
            }
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            if (cboSupplier.SelectedValue == null)
            {
                MessageBox.Show("Выберите поставщика");
                return;
            }

            if (dpSupplyDate.SelectedDate == null)
            {
                MessageBox.Show("Укажите дату поставки");
                return;
            }

            if (cboEmployee.SelectedValue == null)
            {
                MessageBox.Show("Выберите ответственного");
                return;
            }

            if (supplyItemsList.Count == 0)
            {
                MessageBox.Show("Добавьте хотя бы одну позицию поставки");
                return;
            }

            // Проверяем возможность размещения на складах перед созданием поставки
            if (!CanAddAllToStocks(supplyItemsList.ToList()))
            {
                return;
            }

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        string query = @"
                            INSERT INTO public.supplies 
                            (supply_number, supplier_id, supply_date, status, employee_id, total_amount)
                            VALUES (@supply_number, @supplier_id, @supply_date, @status, @employee_id, @total_amount)
                            RETURNING id";

                        var cmd = new NpgsqlCommand(query, conn, transaction);
                        cmd.Parameters.AddWithValue("@supply_number", txtSupplyNumber.Text);
                        cmd.Parameters.AddWithValue("@supplier_id", (int)cboSupplier.SelectedValue);
                        cmd.Parameters.AddWithValue("@supply_date", dpSupplyDate.SelectedDate.Value);
                        cmd.Parameters.AddWithValue("@status", "черновик");
                        cmd.Parameters.AddWithValue("@employee_id", (int)cboEmployee.SelectedValue);
                        cmd.Parameters.AddWithValue("@total_amount", decimal.Parse(txtTotalAmount.Text));

                        int newId = (int)cmd.ExecuteScalar();

                        foreach (var item in supplyItemsList)
                        {
                            string itemQuery = @"
                                INSERT INTO public.supply_items (supply_id, tool_model_id, quantity, purchase_price, total_price, stock_id)
                                VALUES (@supplyId, @toolModelId, @quantity, @price, @totalPrice, @stockId)";
                            var itemCmd = new NpgsqlCommand(itemQuery, conn, transaction);
                            itemCmd.Parameters.AddWithValue("@supplyId", newId);
                            itemCmd.Parameters.AddWithValue("@toolModelId", item.ToolModelId);
                            itemCmd.Parameters.AddWithValue("@quantity", item.Quantity);
                            itemCmd.Parameters.AddWithValue("@price", item.PurchasePrice);
                            itemCmd.Parameters.AddWithValue("@totalPrice", item.TotalPrice);
                            itemCmd.Parameters.AddWithValue("@stockId", item.StockId > 0 ? item.StockId : 1);
                            itemCmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                }

                LoadSupplies();
                ClearForm();
                txtStatus.Text = "Поставка создана";
                MessageBox.Show("Поставка успешно создана!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания поставки: {ex.Message}");
            }
        }

        private void UpdateSupplyStatus(int supplyId, string newStatus)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();

                    DateTime? invoiceDate = null;

                    if (newStatus == "оформлен")
                    {
                        invoiceDate = DateTime.Today;
                    }
                    else if (newStatus == "получен" || newStatus == "отменён")
                    {
                        invoiceDate = DateTime.Today;
                    }

                    string query = "UPDATE public.supplies SET status = @status";

                    if (invoiceDate.HasValue)
                    {
                        query += ", invoice_date = @invoiceDate";
                    }

                    query += " WHERE id = @id";

                    var cmd = new NpgsqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@status", newStatus);
                    cmd.Parameters.AddWithValue("@id", supplyId);

                    if (invoiceDate.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@invoiceDate", invoiceDate.Value);
                    }

                    cmd.ExecuteNonQuery();
                }

                LoadSupplies();
                if (currentSupply != null && currentSupply.Id == supplyId)
                {
                    currentSupply.Status = newStatus;
                    txtStatusDisplay.Text = newStatus;
                    UpdateStatusButtons(newStatus);
                    UpdateEditButton();
                }
                txtStatus.Text = $"Статус изменён на: {newStatus}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка изменения статуса: {ex.Message}");
            }
        }

        private void BtnSetDraft_Click(object sender, RoutedEventArgs e)
        {
            if (currentSupply == null) return;
            if (currentSupply.Status == "получен" || currentSupply.Status == "отменён")
            {
                MessageBox.Show("Нельзя изменить статус полученной или отменённой поставки");
                return;
            }
            var result = MessageBox.Show("Изменить статус на 'Черновик'?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                UpdateSupplyStatus(currentSupply.Id, "черновик");
        }

        private void BtnSetOrdered_Click(object sender, RoutedEventArgs e)
        {
            if (currentSupply == null) return;
            if (currentSupply.Status == "получен" || currentSupply.Status == "отменён")
            {
                MessageBox.Show("Нельзя изменить статус полученной или отменённой поставки");
                return;
            }
            var result = MessageBox.Show("Изменить статус на 'Оформлен'?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                UpdateSupplyStatus(currentSupply.Id, "оформлен");
                LoadSupplies();
            }
        }

        private void BtnSetReceived_Click(object sender, RoutedEventArgs e)
        {
            if (currentSupply == null) return;
            if (currentSupply.Status == "получен")
            {
                MessageBox.Show("Поставка уже имеет статус 'Получен'");
                return;
            }
            if (currentSupply.Status == "отменён")
            {
                MessageBox.Show("Нельзя изменить статус отменённой поставки");
                return;
            }

            // Загружаем позиции поставки для проверки складов
            var itemsToReceive = new List<SupplyItemView>();
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    string query = @"
                        SELECT si.*, tm.name as ToolModelName
                        FROM public.supply_items si
                        JOIN public.tool_models tm ON si.tool_model_id = tm.id
                        WHERE si.supply_id = @supplyId";

                    var cmd = new NpgsqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@supplyId", currentSupply.Id);
                    var dt = new DataTable();
                    dt.Load(cmd.ExecuteReader());

                    foreach (DataRow row in dt.Rows)
                    {
                        itemsToReceive.Add(new SupplyItemView
                        {
                            Id = row.Field<int>("id"),
                            SupplyId = row.Field<int>("supply_id"),
                            ToolModelId = row.Field<int>("tool_model_id"),
                            ToolModelName = row.Field<string>("ToolModelName"),
                            Quantity = row.Field<int>("quantity"),
                            PurchasePrice = row.Field<decimal>("purchase_price"),
                            TotalPrice = row.Field<decimal>("total_price"),
                            StockId = row.IsNull("stock_id") ? 1 : row.Field<int>("stock_id")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки позиций: {ex.Message}");
                return;
            }

            // Проверяем возможность размещения на складах
            if (!CanAddAllToStocks(itemsToReceive))
            {
                return;
            }

            var result = MessageBox.Show("Изменить статус на 'Получен'? Это создаст новые инструменты на складе.", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                UpdateSupplyStatus(currentSupply.Id, "получен");
                CreateToolsFromSupply(currentSupply.Id);
            }
        }

        private void BtnSetClosed_Click(object sender, RoutedEventArgs e)
        {
            if (currentSupply == null) return;
            if (currentSupply.Status == "получен")
            {
                MessageBox.Show("Нельзя отменить полученную поставку");
                return;
            }
            if (currentSupply.Status == "отменён")
            {
                MessageBox.Show("Поставка уже отменена");
                return;
            }
            var result = MessageBox.Show("Изменить статус на 'Отменён'?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                UpdateSupplyStatus(currentSupply.Id, "отменён");
        }

        private void CreateToolsFromSupply(int supplyId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var transaction = conn.BeginTransaction())
                    {
                        var items = new List<SupplyItemView>();
                        string loadQuery = @"
                            SELECT si.*, tm.name as ToolModelName
                            FROM public.supply_items si
                            JOIN public.tool_models tm ON si.tool_model_id = tm.id
                            WHERE si.supply_id = @supplyId";

                        var loadCmd = new NpgsqlCommand(loadQuery, conn, transaction);
                        loadCmd.Parameters.AddWithValue("@supplyId", supplyId);
                        var dt = new DataTable();
                        dt.Load(loadCmd.ExecuteReader());

                        foreach (DataRow row in dt.Rows)
                        {
                            items.Add(new SupplyItemView
                            {
                                Id = row.Field<int>("id"),
                                SupplyId = row.Field<int>("supply_id"),
                                ToolModelId = row.Field<int>("tool_model_id"),
                                ToolModelName = row.Field<string>("ToolModelName"),
                                Quantity = row.Field<int>("quantity"),
                                PurchasePrice = row.Field<decimal>("purchase_price"),
                                TotalPrice = row.Field<decimal>("total_price"),
                                StockId = row.IsNull("stock_id") ? 1 : row.Field<int>("stock_id")
                            });
                        }

                        int totalCreated = 0;

                        foreach (var item in items)
                        {
                            string prefix = GeneratePrefixFromName(item.ToolModelName);
                            int nextNumber = GetNextNumberForPrefix(conn, transaction, prefix);

                            for (int i = 0; i < item.Quantity; i++)
                            {
                                string inventoryNumber = $"{prefix}-{nextNumber:D3}";
                                nextNumber++;

                                string query = @"
                                    INSERT INTO public.tools 
                                    (model_id, inventory_number, status, condition_status, purchase_price, purchase_date, stock_id)
                                    VALUES (@modelId, @inventoryNumber, 'доступен', 'хорошее', @purchasePrice, @purchaseDate, @stockId)";

                                var cmd = new NpgsqlCommand(query, conn, transaction);
                                cmd.Parameters.AddWithValue("@modelId", item.ToolModelId);
                                cmd.Parameters.AddWithValue("@inventoryNumber", inventoryNumber);
                                cmd.Parameters.AddWithValue("@purchasePrice", item.PurchasePrice);
                                cmd.Parameters.AddWithValue("@purchaseDate", DateTime.Today);
                                cmd.Parameters.AddWithValue("@stockId", item.StockId);
                                cmd.ExecuteNonQuery();
                                totalCreated++;
                            }
                        }

                        transaction.Commit();
                        txtStatus.Text = $"Создано инструментов: {totalCreated}";
                        MessageBox.Show($"Успешно создано {totalCreated} инструментов!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка создания инструментов: {ex.Message}");
            }
        }

        private string GeneratePrefixFromName(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
                return "ИНВ";

            string prefix = "";
            int count = 0;

            foreach (char c in modelName)
            {
                if (char.IsLetter(c))
                {
                    prefix += char.ToUpper(c);
                    count++;
                    if (count >= 3)
                        break;
                }
            }

            while (prefix.Length < 3)
            {
                prefix += "X";
            }

            return prefix;
        }

        private int GetNextNumberForPrefix(NpgsqlConnection conn, NpgsqlTransaction transaction, string prefix)
        {
            try
            {
                string query = @"
                    SELECT COALESCE(MAX(CAST(SUBSTRING(inventory_number FROM '-([0-9]+)$') AS INTEGER)), 0)
                    FROM public.tools 
                    WHERE inventory_number LIKE @pattern";

                var cmd = new NpgsqlCommand(query, conn, transaction);
                cmd.Parameters.AddWithValue("@pattern", $"{prefix}-%");
                var result = cmd.ExecuteScalar();

                if (result != null && result != DBNull.Value)
                {
                    return Convert.ToInt32(result) + 1;
                }

                return 1;
            }
            catch
            {
                return 1;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void DgSupplies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgSupplies.SelectedItem is SupplyView selected)
            {
                currentSupply = selected;
                DisplaySupplyInForm(selected);
                LoadSupplyItems(selected.Id);
                UpdateStatusButtons(selected.Status);
                UpdatePositionButtons(supplyItemsList.Count > 0);
                UpdateEditButton();
                txtStatus.Text = $"Выбрана поставка №{selected.SupplyNumber}";
            }
        }

        private void DisplaySupplyInForm(SupplyView supply)
        {
            txtSupplyNumber.Text = supply.SupplyNumber;
            cboSupplier.SelectedValue = supply.SupplierId;
            dpSupplyDate.SelectedDate = supply.SupplyDate;
            txtStatusDisplay.Text = supply.Status;
            cboEmployee.SelectedValue = supply.EmployeeId;
            txtTotalAmount.Text = supply.TotalAmount.ToString("N2");

            bool isEditable = supply.Status == "черновик";
            cboSupplier.IsEnabled = isEditable;
            dpSupplyDate.IsEnabled = isEditable;
            cboEmployee.IsEnabled = isEditable;

            UpdateStatusButtons(supply.Status);
            btnCreate.IsEnabled = false;
        }

        private void ClearForm()
        {
            GenerateSupplyNumber();
            cboSupplier.SelectedIndex = -1;
            dpSupplyDate.SelectedDate = DateTime.Today;
            txtStatusDisplay.Text = "черновик";
            cboEmployee.SelectedIndex = -1;
            txtTotalAmount.Text = "0.00";
            supplyItemsList.Clear();
            currentSupply = null;
            editingSupplyId = null;
            isEditMode = false;

            cboSupplier.IsEnabled = true;
            dpSupplyDate.IsEnabled = true;
            cboEmployee.IsEnabled = true;

            btnSetDraft.IsEnabled = false;
            btnSetOrdered.IsEnabled = false;
            btnSetReceived.IsEnabled = false;
            btnSetClosed.IsEnabled = false;
            btnCreate.IsEnabled = false;
            btnEditSupply.IsEnabled = false;
            btnEditItem.IsEnabled = false;
            btnDeleteItem.IsEnabled = false;

            dgSupplies.SelectedItem = null;
            txtStatus.Text = "Форма очищена для создания новой поставки";
        }

        private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SupplyItemDialog(connString, -1, supplyItemsList.Select(x => x.ToolModelId).ToList());
            if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
            {
                supplyItemsList.Add(dialog.SelectedItem);
                UpdateCreateButton();
                UpdatePositionButtons(true);

                decimal total = supplyItemsList.Sum(x => x.TotalPrice);
                txtTotalAmount.Text = total.ToString("N2");
                txtStatus.Text = "Позиция добавлена";
            }
        }

        private void BtnEditItem_Click(object sender, RoutedEventArgs e)
        {
            if (dgSupplyItems.SelectedItem is SupplyItemView selectedItem)
            {
                var existingToolModelIds = supplyItemsList
                    .Where(x => x.ToolModelId != selectedItem.ToolModelId)
                    .Select(x => x.ToolModelId)
                    .ToList();

                var dialog = new SupplyItemDialog(connString, -1, existingToolModelIds, selectedItem);
                if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
                {
                    var index = supplyItemsList.IndexOf(selectedItem);
                    supplyItemsList[index] = dialog.SelectedItem;

                    decimal total = supplyItemsList.Sum(x => x.TotalPrice);
                    txtTotalAmount.Text = total.ToString("N2");
                    txtStatus.Text = "Позиция обновлена";
                }
            }
            else
            {
                MessageBox.Show("Выберите позицию для редактирования");
            }
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (dgSupplyItems.SelectedItem is SupplyItemView selectedItem)
            {
                var result = MessageBox.Show($"Удалить позицию с моделью \"{selectedItem.ToolModelName}\"?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    supplyItemsList.Remove(selectedItem);
                    UpdateCreateButton();
                    UpdatePositionButtons(supplyItemsList.Count > 0);

                    decimal total = supplyItemsList.Sum(x => x.TotalPrice);
                    txtTotalAmount.Text = total.ToString("N2");
                    txtStatus.Text = "Позиция удалена";
                }
            }
            else
            {
                MessageBox.Show("Выберите позицию для удаления");
            }
        }
    }

    public class Supply
    {
        public int Id { get; set; }
        public string SupplyNumber { get; set; }
        public int SupplierId { get; set; }
        public DateTime SupplyDate { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public int? EmployeeId { get; set; }
    }

    public class SupplyItem
    {
        public int Id { get; set; }
        public int SupplyId { get; set; }
        public int ToolModelId { get; set; }
        public int Quantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal TotalPrice { get; set; }
    }

    public class SupplyView : Supply
    {
        public string SupplierName { get; set; }
        public string EmployeeFIO { get; set; }
    }

    public class SupplyItemView : SupplyItem
    {
        public string ToolModelName { get; set; }
        public int StockId { get; set; }
        public string StockName { get; set; }
    }
}