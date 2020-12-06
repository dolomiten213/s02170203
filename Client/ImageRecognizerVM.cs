﻿
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using Contracts;
using System.Net.Http;
using Newtonsoft.Json;
using System.Text;
using Microsoft.AspNetCore.SignalR.Client;

namespace WPF
{
    public class ImageRecognizerVM : BaseViewModel
    {

//===========================================================================================//
        
        public ObservableCollection<Recognition> Recognitions { get; set; }
        public List<Photo> Photos { get; set; }

//===========================================================================================//

        public string ControlButtonContent
        {
            get
            {
                return IsRunning ? "Stop" : "Start";
            }
        }
        public bool ControlButtonEnabled
        {
            get
            {
                return !IsStopping;
            }
        }
        public bool ClearButtonEnabled
        {
            get
            {
                return !IsClearing;
            }
        }

//===========================================================================================// 

        private string imagesPath = @"D:\Pictires\images";
        public string ImagesPath
        {
            get
            {
                return imagesPath;
            }
            set
            {
                imagesPath = value;
                OnPropertyChanged(nameof(ImagesPath));
            }
        }

        private string onnxModelPath = @"D:\Downloads\resnet34-v1-7.onnx";
        public string OnnxModelPath
        {
            get
            {
                return onnxModelPath;
            }
            set
            {
                onnxModelPath = value;
                OnPropertyChanged(nameof(OnnxModelPath));
            }
        }

        private int imagesCount = 1;
        public int ImagesCount
        {
            get
            {
                return imagesCount;
            }
            set
            {
                imagesCount = value;
                OnPropertyChanged(nameof(ImagesCount));
            }
        }

        private int imagesCounter = 0;
        public int ImagesCounter
        {
            get
            {
                return imagesCounter;
            }
            set
            {
                imagesCounter = value;
                OnPropertyChanged(nameof(ImagesCounter));
            }
        }

        private bool isRunning = false;
        public bool IsRunning
        {
            get
            {
                return isRunning;
            }
            set
            {
                isRunning = value;
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(ControlButtonContent));
            }
        }

        private bool isStopping = false;
        public bool IsStopping
        {
            get
            {
                return isStopping;
            }
            set
            {
                isStopping = value;
                OnPropertyChanged(nameof(IsStopping));
                OnPropertyChanged(nameof(ControlButtonEnabled));
            }
        }

        private bool isClearing = false;
        public bool IsClearing
        {
            get
            {
                return isClearing;
            }
            set
            {
                isClearing = value;
                OnPropertyChanged(nameof(IsClearing));
                OnPropertyChanged(nameof(ClearButtonEnabled));
            }
        }

//===========================================================================================// 

        private string[] images = null;
        private HttpClient client = null;
        private HubConnection connection = null;

//===========================================================================================// 

        public ImageRecognizerVM()
        {
            client = new HttpClient();
            Recognitions = new ObservableCollection<Recognition>();

            connection = new HubConnectionBuilder()
                .WithUrl("http://localhost:5000/recognitionhub")
                .Build();
            connection.On<string, string>("RealTimeAdd", Add);
            connection.StartAsync();
            

            LoadRecognitionsFromServerAsync();
            Photos = new List<Photo>();
        }

//===========================================================================================//

        public async Task StartAsync()
        {
            IsRunning = true;
            
            if (connection.State == HubConnectionState.Disconnected)
            {
                await TryReconnectToserver();
                await LoadAsync();
            }         

            images = Directory.GetFiles(ImagesPath);
            
            Photos = await GenerateImagesFromPathsAsync();
            Photos = await RemoveRecognizedImagesAsync();
            
            if(Photos.Count == 0)
            {
                IsRunning = false;
                return;
            }

            ImagesCount = Photos.Count;
            ImagesCounter = 0;

            var data = await PrepareDataForSendingAsync();
            var url = "http://localhost:5000/recognition/start/";
            
            //var isRecognizing = true;

            //var periodicGetToServer = Task.Factory.StartNew(async () =>
            //{
            //    while (isRecognizing)
            //    {
            //        AddNewRecognitionsToView(await GetNewRecognitionsFromServerAsync());
            //        await Task.Run(() => System.Threading.Thread.Sleep(500));
            //    }
            //});

            await client.PostAsync(url, data);
            
            //isRecognizing = false;

            //AddNewRecognitionsToView(await GetNewRecognitionsFromServerAsync());

            IsStopping = true;
            
            await SaveRecognitionsOnServerAsync();
            Photos.Clear();
            
            IsStopping = false;
            IsRunning = false;
        }
        public async Task StopAsync()
        {
            IsStopping = true;

            var url = "http://localhost:5000/recognition/stop/";
            await client.PostAsync(url, null);
        }
        public async Task ClearAsync()
        {
            IsClearing = true;
            Recognitions.Clear();
            Photos.Clear();

            var url = "http://localhost:5000/recognition/clear/";
            await client.PutAsync(url, null);
            
            IsClearing = false;
        }
        public async Task LoadAsync()
        {
            await LoadRecognitionsFromServerAsync();
        }

//===========================================================================================//
        
        private async Task<StringContent> PrepareDataForSendingAsync()
        {
            return await Task.Run(() =>
            {
                List<Photo> sendPhotos = new List<Photo>();
                foreach (var p in Photos)
                {
                    sendPhotos.Add(new Photo
                    {
                        IsSavedInDataBase = false,
                        Path = p.Path,
                        Pixels = p.Pixels,
                        Image = null
                    });
                }

                var b = new StartOptions
                {
                    Onnx = OnnxModelPath,
                    Images = sendPhotos,
                };

                var json = JsonConvert.SerializeObject(b);
                return new StringContent(json, Encoding.UTF8, "application/json");
            });                         
        }
        private async Task<List<Recognition>> GetNewRecognitionsFromServerAsync()
        {
            using var ans = await client.GetAsync("http://localhost:5000/recognition/add/");
            var resp = await ans.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<List<Recognition>>(resp);
        }
        private void AddNewRecognitionsToView(List<Recognition> predictions)
        {           

            if (predictions.Count == 0) return;
            
            foreach(var prediction in predictions)
            {
                var l = (from pic in Recognitions
                         where pic.Title == prediction.Title
                         select pic).FirstOrDefault();
                
                List<Photo> a = new List<Photo>();
                
                foreach(var photo in prediction.Photos)
                {
                    a.Add(new Photo
                    {
                        IsSavedInDataBase = true,
                        Path = photo.Path,
                        Pixels = (from p in Photos
                                  where p.Path == photo.Path
                                  select p.Pixels).SingleOrDefault(),

                        Image = (from p in Photos
                                 where p.Path == photo.Path
                                 select p.Image).SingleOrDefault(),
                    });
                }                                                                                                                          
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (l == null) //first time 
                    {
                        var b = new ObservableCollection<Photo>();
                        foreach (var ph in a)
                        {
                            b.Add(ph);
                        }
                        Recognitions.Add(new Recognition
                        {
                            Title = prediction.Title,
                            Count = b.Count,
                            Photos = b,
                        });
                    }
                    else
                    {
                        int index = Recognitions.IndexOf(l);
                        Recognitions[index].Count += a.Count;
                        foreach (var ph in a)
                        {
                            Recognitions[index].Photos.Add(ph);
                        }
                    }
                    ImagesCounter += a.Count;
                });               
            }
        }
        private void Add(string title, string path)
        {
            lock (Recognitions)
            {
                var l = (from pic in Recognitions
                         where pic.Title == title
                         select pic).FirstOrDefault();

                var q = (from pic in Photos
                         where path == pic.Path
                         select pic).FirstOrDefault();
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (l == null) //first time 
                    {
                        Recognitions.Add(new Recognition
                        {
                            Title = title,
                            Count = 1,
                            Photos = new ObservableCollection<Photo> { q }
                        });
                    }
                    else
                    {
                        int index = Recognitions.IndexOf(l);
                        Recognitions[index].Count++;
                        Recognitions[index].Photos.Add(q);
                    }
                    ImagesCounter++;
                });
            }
        }

//===========================================================================================//       
        
        private async Task SaveRecognitionsOnServerAsync()
        {
            var url = "http://localhost:5000/recognition/save/";
            await client.PutAsync(url, null);
        }       
        private async Task LoadRecognitionsFromServerAsync()
        {
            if (Recognitions.Count != 0) return;
            using var ans = await client.GetAsync("http://localhost:5000/recognition/load");
            var recognitions = JsonConvert.DeserializeObject<ObservableCollection<Recognition>>(await ans.Content.ReadAsStringAsync());
            foreach (var r in recognitions)
            {
                ObservableCollection<Photo> a = new ObservableCollection<Photo>();
                foreach (var photo in r.Photos)
                {
                    a.Add(new Photo
                    {
                        IsSavedInDataBase = true,
                        Path = photo.Path,
                        Pixels = photo.Pixels,
                        Image = ByteToImage(photo.Pixels)
                    });
                }
                Recognitions.Add(new Recognition
                {
                    Count = r.Photos.Count,
                    Title = r.Title,
                    Photos = a,
                });
            }
        }
        private async Task TryReconnectToserver()
        {
            await connection.StartAsync();
            while (connection.State == HubConnectionState.Connecting || connection.State == HubConnectionState.Reconnecting)
            {
                using var t = Task.Run(() => System.Threading.Thread.Sleep(500));
                await t;
            }
            if (connection.State == HubConnectionState.Disconnected) throw new Exception("Не удалось подключиться к серверу");
        }

//===========================================================================================//

        private BitmapImage ByteToImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
            {
                throw new Exception("null");
            }

            var image = new BitmapImage();
            using (var mem = new MemoryStream(imageData))
            {
                mem.Position = 0;
                image.BeginInit();
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = null;
                image.StreamSource = mem;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
        private byte[] ImagetoByte(Object bmpa)
        {
            var bmp = (BitmapImage)bmpa;
            byte[] data;
            JpegBitmapEncoder encoder = new JpegBitmapEncoder();
            try
            {
                encoder.Frames.Add(BitmapFrame.Create(bmp));
            }
            catch { }

            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                data = ms.ToArray();
            }
            return data;
        }
        private async Task<List<Photo>> GenerateImagesFromPathsAsync()
        {
            return await Task.Run(async () =>
            {
                Task<Photo>[] tasks = new Task<Photo>[images.Length];

                for (int i = 0; i < images.Length; i++)
                {
                    tasks[i] = Task<Photo>.Factory.StartNew((imagePath) =>
                    {
                        string path = (string)imagePath;
                        BitmapImage bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path);
                        bmp.DecodePixelHeight = 300;
                        bmp.EndInit();
                        bmp.Freeze();

                        return new Photo
                        {
                            IsSavedInDataBase = false,
                            Path = path,
                            Pixels = ImagetoByte(bmp),
                            Image = bmp
                        };

                    }, images[i]);
                }
                await Task.WhenAll(tasks);

                var t = Task<List<Photo>>.Factory.StartNew(() =>
                {
                    List<Photo> a = new List<Photo>();
                    foreach (Task<Photo> t in tasks)
                    {
                        a.Add(t.Result);
                    }
                    return a;
                });
                return await t;
            });

        }
        private async Task<List<Photo>> RemoveRecognizedImagesAsync()
        {
            return await Task.Run(() =>
            {
                for (int i = 0; i < Photos.Count; i++)
                {
                    var q = from rec in Recognitions
                            from ph in rec.Photos
                            where ph.Path == Photos[i].Path
                            select ph.Pixels;

                    if (q != null)
                    {
                        var a = Photos[i].Pixels;

                        foreach (byte[] pxls in q)
                        {
                            if (pxls.Length == a.Length)
                            {
                                for (int j = 0; j < a.Length; j++)
                                {
                                    if (pxls[j] != a[j])
                                    {
                                        break;
                                    }
                                    if (j == a.Length - 1)
                                    {
                                        Photos[i].Path = null;
                                    }
                                }
                            }
                        }
                    }
                }
                return (from a in Photos
                        where a.Path != null
                        select a).ToList();
            });
        }

//===========================================================================================//    
    }
}
