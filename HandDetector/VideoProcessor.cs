﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using CURELab.SignLanguage.HandDetector.Annotations;
using CURELab.SignLanguage.HandDetector.Model;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.UI;
using Microsoft.Kinect;
using Skeleton = Microsoft.Kinect.Skeleton;

namespace CURELab.SignLanguage.HandDetector
{
    internal class VideoProcessor : INotifyPropertyChanged
    {
        public static double DIFF = 10;
        private static VideoProcessor m_VideoProcessor;
        private OpenCVController m_opencv;
        private ImageViewer viewer;
        private System.Drawing.Point rightHandPosition;
        private System.Drawing.Point headPosition;

        public static double CullingThresh;
        public static float AngleRotateTan = AaronRotateTan;
        // demo
        public const float DemoRotateTan = 0.45f;
        // anita
        public const float AnitaRotateTan = 0.3f;
        // michael
        public const float MichaelRotateTan = 0.23f;
        // Aaron
        public const float AaronRotateTan = 0.28f;

        private const int handShapeWidth = 60;
        private const int handShapeHeight = 60;
        private const int FrameWidth = 640;
        private const int FrameHeight = 480;
        private List<MySkeleton> sktList;
        private int currentFrame;
        Capture _CCapture;
        Capture _DCapture;
        private Image<Bgr, byte> CurrentImageC;
        private Image<Bgr, byte> CurrentImageD;

        public double FrameRate = 0;
        public int TotalFrames = 0;

        int FrameCount;

        private WriteableBitmap colorWriteBitmap;

        public WriteableBitmap ColorWriteBitmap
        {
            get { return colorWriteBitmap; }
            protected set { colorWriteBitmap = value; }
        }

        private WriteableBitmap depthWriteBitmap;

        public WriteableBitmap DepthWriteBitmap
        {
            get { return depthWriteBitmap; }
            protected set { depthWriteBitmap = value; }
        }

        private WriteableBitmap processedDepthBitmap;

        public WriteableBitmap ProcessedDepthBitmap
        {
            get { return processedDepthBitmap; }
            protected set { processedDepthBitmap = value; }
        }

        private WriteableBitmap grayWriteBitmap;

        public WriteableBitmap GrayWriteBitmap
        {
            get { return grayWriteBitmap; }
            protected set { grayWriteBitmap = value; }
        }

        public WriteableBitmap WrtBMP_RightHandFront { get; set; }
        public WriteableBitmap WrtBMP_LeftHandFront { get; set; }
        public WriteableBitmap WrtBMP_Candidate2 { get; set; }
        public WriteableBitmap WrtBMP_Candidate3 { get; set; }
        public WriteableBitmap WrtBMP_Candidate1 { get; set; }
        public WriteableBitmap WrtBMP_RightHandSide { get; set; }
        public WriteableBitmap WrtBMP_LeftHandSide { get; set; }

        private VisualData vs = VisualData.GetSingleton();
        public int CurrentFrame
        {
            get { return currentFrame; }
            set
            {
                currentFrame = value;
                vs.CurrentFrame = value;
            }
        }

        protected VideoProcessor()
        {
            m_opencv = OpenCVController.GetSingletonInstance();
            viewer = new ImageViewer();
            viewer.Show();
            this.ColorWriteBitmap = new WriteableBitmap(FrameWidth, FrameHeight, 96.0, 96.0,
                System.Windows.Media.PixelFormats.Bgr24, null);
            this.DepthWriteBitmap = new WriteableBitmap(FrameWidth, FrameHeight, 96.0, 96.0,
                System.Windows.Media.PixelFormats.Bgr24, null);
            this.WrtBMP_RightHandFront = new WriteableBitmap(handShapeWidth, handShapeHeight, 96.0, 96.0,
                System.Windows.Media.PixelFormats.Gray8, null);
            this.WrtBMP_LeftHandFront = new WriteableBitmap(handShapeWidth, handShapeHeight, 96.0, 96.0,
                System.Windows.Media.PixelFormats.Gray8, null);

            rightHandPosition = new System.Drawing.Point();
        }

        public static VideoProcessor GetSingletonInstance()
        {
            return m_VideoProcessor ?? (m_VideoProcessor = new VideoProcessor());
        }

        public enum StreamType
        {
            Color,
            Depth
        }
        public void OpenVideoFile(string path, StreamType st)
        {
            var c = new Capture(path);
            FrameRate = c.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_FPS);
            TotalFrames = (int)c.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_FRAME_COUNT);
            //The four_cc returns a double so we must convert it
            double codec_double = c.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_FOURCC);
            if (st == StreamType.Color)
            {
                if (_CCapture != null)
                {
                    _CCapture.Dispose();//dispose of current capture
                }
                _CCapture = c;
            }
            else
            {
                if (_DCapture != null)
                {
                    _DCapture.Dispose();//dispose of current capture
                }
                _DCapture = c;
            }

        }

        public void OpenSkeleton(string path)
        {
            using (CsvFileReader reader = new CsvFileReader(path))
            {
                sktList = new List<MySkeleton>();
                CsvRow row = new CsvRow();
                // one frame skeleton
                while (reader.ReadRow(row))
                {
                    var skt = new MySkeleton();
                    int step = 9;
                    // one joint
                    for (int i = 0; i < row.Count; i += step)
                    {
                        var type = i / step;
                        var joint = new MyJoint()
                        {
                            Pos3D = new SkeletonPoint()
                            {
                                X = Convert.ToSingle(row[i]),
                                Y = Convert.ToSingle(row[i + 1]),
                                Z = Convert.ToSingle(row[i + 2])
                            },
                            PosDepth = new System.Drawing.Point()
                            {
                                X = (int)Convert.ToSingle(row[i + 3]),
                                Y = (int)Convert.ToSingle(row[i + 4])
                            },
                            PosColor = new System.Drawing.Point()
                            {
                                X = (int)Convert.ToSingle(row[i + 5]),
                                Y = (int)Convert.ToSingle(row[i + 6])
                            }
                        };
                        skt[type] = joint;
                    }
                    sktList.Add(skt);
                }
            }
            //foreach (var mySkeleton in sktList)
            //{
            //    Console.Write(mySkeleton[MyJointType.Head].PosColor.X);
            //}
        }



        private int headDepth;
        public void ProcessFrame()
        {
            if (_CCapture != null)
            {
                var r = _CCapture.Grab();
                var imgc = _CCapture.RetrieveBgrFrame();
                ImageConverter.UpdateWriteBMP(colorWriteBitmap, imgc.ToBitmap());

            }
            if (_DCapture != null)
            {
                var r = _DCapture.Grab();
                var img = _DCapture.RetrieveBgrFrame();
                int framenumber = (int)_DCapture.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_POS_FRAMES);
                //Show time stamp
                double time_index = _DCapture.GetCaptureProperty(Emgu.CV.CvEnum.CAP_PROP.CV_CAP_PROP_POS_MSEC);
                //UpdateTextBox("Frame: " + framenumber.ToString(), Frame_lbl);
                if (CurrentFrame >= sktList.Count)
                {
                    CurrentFrame = sktList.Count - 1;
                }
                headPosition = sktList[CurrentFrame][MyJointType.Head].PosDepth;
                if (img == null)
                {
                    headDepth = 130;
                }
                else
                {
                    // img[row,column]
                    headDepth = (int)img[headPosition.Y, headPosition.X].Blue;
                }
                headDepth = (int)img[85, 315].Blue;
                //***********cull image*****************
                double cull = headDepth - CullingThresh;
                var depthImg = img.ThresholdToZeroInv(new Bgr(cull, cull, cull));
                //Image<Gray, Byte> depthImg = img.Convert<Gray, byte>().ThresholdBinary(new Gray(160), new Gray(255));
                viewer.Image = depthImg;
                var sw = Stopwatch.StartNew();


                Image<Gray, Byte> rightFront = null;
                Image<Gray, Byte> leftFront = null;

                PointF rightVector = new PointF(-10, -10);
                PointF leftVector = new PointF(10, -10);
                bool isSkip = false;
                bool leftHandRaise = false;


                //if (skeletons != null && skeletons[0].TrackingState == SkeletonTrackingState.Tracked)
                //{
                //    PointF hr = SkeletonPointToScreen(skeletons[0].Joints[JointType.HandRight].Position);
                //    PointF hl = SkeletonPointToScreen(skeletons[0].Joints[JointType.HandLeft].Position);
                //    PointF er = SkeletonPointToScreen(skeletons[0].Joints[JointType.ElbowRight].Position);
                //    PointF el = SkeletonPointToScreen(skeletons[0].Joints[JointType.ElbowLeft].Position);
                //    PointF hip = SkeletonPointToScreen(skeletons[0].Joints[JointType.HipCenter].Position);
                //    // hand is lower than hip
                //    //Console.WriteLine(skeletons[0].Joints[JointType.HandRight].Position.Y);
                //    //Console.WriteLine(skeletons[0].Joints[JointType.HipCenter].Position.Y);
                //    //Console.WriteLine("-------------");
                //    if (skeletons[0].Joints[JointType.HandRight].Position.Y <
                //        skeletons[0].Joints[JointType.HipCenter].Position.Y + 0.05)
                //    {
                //        isSkip = true;
                //    }
                //    if (skeletons[0].Joints[JointType.HandLeft].Position.Y >
                //        skeletons[0].Joints[JointType.HipCenter].Position.Y)
                //    {
                //        leftHandRaise = true;
                //    }

                //    rightVector.X = (hr.X - er.X);
                //    rightVector.Y = (hr.Y - er.Y);
                //    leftVector.X = (hl.X - el.X);
                //    leftVector.Y = (hl.Y - el.Y);
                //}
                HandShapeModel handModel = null;
                Console.WriteLine(headDepth);
                int handDepth = (int)(3200.0 / 255 * headDepth + 800);
                Console.WriteLine(handDepth);
                if (!isSkip)
                {
                    handModel = m_opencv.FindHandPart(ref depthImg, out rightFront, out leftFront, handDepth, rightVector, leftVector, leftHandRaise);
                }
                viewer.Image = depthImg;


                // no hands detected
                if (handModel == null)
                {
                    handModel = new HandShapeModel(0, HandEnum.None);
                }
                //sw.Restart();

                // database processing
                //DBManager db = DBManager.GetSingleton();
                //if (db != null)
                //{
                //    if (skeletons != null)
                //    {
                //        handModel.SetSkeletonData(skeletons[0]);
                //    }
                //    db.AddFrameData(handModel);
                //}
                //// not recording show prob
                //else
                //{
                //Image<Bgr, byte>[] result = HandShapeClassifier.GetSingleton()
                //.RecognizeGesture(handModel.hogRight, 3);
                ////Console.WriteLine(sw.ElapsedMilliseconds);
                //if (result != null)
                //{
                //    ImageConverter.UpdateWriteBMP(WrtBMP_Candidate1, result[0].Convert<Gray, byte>().ToBitmap());
                //    ImageConverter.UpdateWriteBMP(WrtBMP_Candidate2, result[1].Convert<Gray, byte>().ToBitmap());
                //    ImageConverter.UpdateWriteBMP(WrtBMP_Candidate3, result[2].Convert<Gray, byte>().ToBitmap());
                //}
                //}
                //string currentSign = db == null ? "0" : db.CurrentSign.ToString();
                //string path = @"J:\Kinect data\Aaron 141-180\hands\" + currentSign + " " + handModel.frame.ToString();
                //// UI update
                //if (rightFront != null)
                //{
                //    Bitmap right = rightFront.ToBitmap();
                //    //right.Save(path + " r.jpg");
                //    ImageConverter.UpdateWriteBMP(WrtBMP_RightHandFront, right);
                //}
                //if (leftFront != null)
                //{
                //    Bitmap left = leftFront.ToBitmap();
                //    //left.Save(path + " l.jpg");
                //    ImageConverter.UpdateWriteBMP(WrtBMP_LeftHandFront, left);
                //}
                //if (sw.ElapsedMilliseconds > 15)
                //{
                //    Console.WriteLine("Find hand:" + sw.ElapsedMilliseconds);
                //}
                Console.WriteLine("Find hand:" + sw.ElapsedMilliseconds);
                sw.Restart();
                ImageConverter.UpdateWriteBMP(depthWriteBitmap, img.ToBitmap());
                CurrentFrame = framenumber-1;
            }
        }

        public void SetCurrentFrame(int index)
        {
            if (_DCapture != null)
            {
                _DCapture.SetCaptureProperty(CAP_PROP.CV_CAP_PROP_POS_FRAMES, index);
            }
            if (_CCapture != null)
            {
                _CCapture.SetCaptureProperty(CAP_PROP.CV_CAP_PROP_POS_FRAMES, index);
            }
            ProcessFrame();
        }

        #region INotifyPropertyChanged 成员

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                this.PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}