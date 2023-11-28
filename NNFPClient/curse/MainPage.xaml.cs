using curse.NNSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
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

namespace curse
{
    /// <summary>
    /// Логика взаимодействия для MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
        }

        async private void Login_Click(object sender, RoutedEventArgs e)
        {
            TcpClient client = new TcpClient(Host.Text, 2920);
            var s = client.GetStream();
            var pass = Password.Text;
            var user = Name.Text;
            var Buser = Encoding.UTF8.GetBytes(user);
            var Bpass = Encoding.UTF8.GetBytes(pass);
            #region проверка имени 
            await s.WriteMessage(OutputFrameType.Login, Buser);
            var length = await s.ReadInt();
            var input = await s.ReadMessageType();
            switch (input)
            {
                case InputFrameType.AuthCheckData:
                    break;
                case InputFrameType.AuthFailure:
                    await s.WriteAsync(new byte[6]);
                    Name.Text="Нет такого пользователя";
                    client.Close();
                    return;
                default: return;
            }
            #endregion
            byte[] salt = new byte[length];
            await s.ReadExactBytes(salt);
            var code = MD5.HashData(salt.Concat(Bpass).ToArray());
            await s.WriteMessage(OutputFrameType.Auth, code);
            length=await s.ReadInt();
            input= await s.ReadMessageType();
            if (input == InputFrameType.AuthFailure)
            {
                await s.WriteAsync(new byte[6]);
                Password.Text="Нет такого пароля";
                client.Close();
                return;
            }

            NavigationService.Navigate(new WindowPage());
        }
    }
}
