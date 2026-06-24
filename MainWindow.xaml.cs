using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Rental
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            HelpWindow helpWindow = new HelpWindow();
            helpWindow.Show();
        }

        private void btnAddSupplier_Click(object sender, RoutedEventArgs e)
        {
            AddSupplier addSupplier = new AddSupplier();
            addSupplier.Show();
        }

        private void btnAddDeliveryCity_Click(object sender, RoutedEventArgs e)
        {
            AddDeliveryCity addDeliveryCity = new AddDeliveryCity();
            addDeliveryCity.Show();
        }

        private void btnAddCategory_Click(object sender, RoutedEventArgs e)
        {
            AddCategory addCategory = new AddCategory();
            addCategory.Show();
        }

        private void btnAddClient_Click(object sender, RoutedEventArgs e)
        {
            AddClient addClient = new AddClient();
            addClient.Show();
        }

        private void btnAddEmployee_Click(object sender, RoutedEventArgs e)
        {
            AddEmployee addEmployee = new AddEmployee();
            addEmployee.Show();
        }

        private void btnAddToolAndModel_Click(object sender, RoutedEventArgs e)
        {
            AddTool addTool = new AddTool();
            addTool.Show();
        }

        private void btnAddWarehouse_Click(object sender, RoutedEventArgs e)
        {
            AddStock addStock = new AddStock();
            addStock.Show();
        }

        private void btnSupplies_Click(object sender, RoutedEventArgs e)
        {
            Supplies supplies = new Supplies();
            supplies.Show();
        }

        private void btnWriteOff_Click(object sender, RoutedEventArgs e)
        {
            WriteOff writeOff = new WriteOff();
            writeOff.Show();
        }

        private void btnRepair_Click(object sender, RoutedEventArgs e)
        {
            Repair repair = new Repair();
            repair.Show();
        }

        private void btnRentalContract_Click(object sender, RoutedEventArgs e)
        {
            RentalForm rentalForm = new RentalForm();
            rentalForm.Show();
        }

        private void btnRatingModels_Click(object sender, RoutedEventArgs e)
        {
            ReportForm reportForm = new ReportForm();
            reportForm.ShowDialog();
        }

        private void btnReturn_Click(object sender, RoutedEventArgs e)
        {
            Return rreturn = new Return();
            rreturn.Show();
        }

        private void btnMovement_Click(object sender, RoutedEventArgs e)
        {
            Movement movement = new Movement();
            movement.Show();
        }

        private void btnToolHistory_Click(object sender, RoutedEventArgs e)
        {
            ReportForm reportForm = new ReportForm();
            reportForm.ShowDialog();
        }
    }
}
